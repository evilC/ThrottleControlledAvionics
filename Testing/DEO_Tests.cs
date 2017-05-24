﻿//   DEO_Tests.cs
//
//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2017 Allis Tauri
using System;
using AT_Utils;

namespace ThrottleControlledAvionics
{
    public abstract class LND_Test_Base : TCA_Test
    {
        protected string Save = "";
        protected Autopilot2 program = Autopilot2.None;
        protected RealTimer delay = new RealTimer(5);

        bool level_loaded;
        void onLevelWasLoaded(GameScenes scene) { level_loaded = true; }

        enum Stage { LOAD, WAIT_FOR_LEVEL, CREATE_TARGET, LAND, FINISH }
        Stage stage;

        public override string Setup()
        {
            GameEvents.onLevelWasLoadedGUIReady.Add(onLevelWasLoaded);
            stage = Stage.LOAD;
            return null;
        }

        protected abstract bool GetModule();
        protected abstract void CreateTarget(System.Random RND);
        protected abstract void OnLand();

        public override bool Update(Random RND)
        {
            Status = stage.ToString().Replace("_", " ");
            switch(stage)
            {
            case Stage.LOAD:
                level_loaded = false;
                ScenarioTester.LoadGame(Save);
                stage = Stage.WAIT_FOR_LEVEL;
                delay.Reset();
                break;
            case Stage.WAIT_FOR_LEVEL:
                if(level_loaded && 
                   FlightGlobals.ready && 
                   FlightGlobals.ActiveVessel != null &&
                   !FlightGlobals.ActiveVessel.packed)
                {
                    if(!delay.TimePassed) break;
                    if(!GetTCA()) 
                    {
                        Utils.Message("TCA is not installed/enabled on the ship");
                        return false;
                    }
                    if(!GetModule()) 
                    {
                        Utils.Message("{0} module is not installed on the ship", program);
                        return false;
                    }
                    VSL.SetTarget(null);
                    stage = Stage.CREATE_TARGET;
                }
                break;
            case Stage.CREATE_TARGET:
                if(CFG.Target == null)
                {
                    CreateTarget(RND);
                    CheatOptions.InfinitePropellant = false;
                    CheatOptions.InfiniteElectricity = true;
                    VSL.Engines.ActivateEnginesAndRun(() => CFG.AP2.XOn(program));
                    break;
                }
                if(CFG.AP2[program])
                {
                    MapView.EnterMapView();
                    VSL.vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                    stage = Stage.LAND;
                }
                break;
            case Stage.LAND:
                CFG.WarpToNode = true;
                if(VSL == null || VSL.vessel == null || 
                   VSL.vessel.state == Vessel.State.DEAD)
                {
                    stage = Stage.FINISH;
                    delay.Reset();
                    break;
                }
                if(CFG.AP2[program]) 
                {
                    OnLand();
                    break;
                }
                FlightCameraOverride.AnchorForSeconds(FlightCameraOverride.Mode.OrbitAround, VSL.vessel.transform, delay.Period+1, true);
                CFG.AP2.XOff();
                stage = Stage.FINISH;
                delay.Reset();
                break;
            case Stage.FINISH:
                if(!delay.TimePassed) break;
                Utils.Log("{}, Done.", GetType().Name);//debug
                Cleanup();
                Setup();
                break;
            }
            return true;
        }

        public override void Cleanup()
        {
            if(TCA != null && CFG != null) CFG.AP2.XOff();
            GameEvents.onLevelWasLoadedGUIReady.Remove(onLevelWasLoaded);
        }

        public override bool NeedsFixedUpdate { get { return false; } }
        public override bool NeedsUpdate { get { return true; } }
    }

    public abstract class DEO_Test_Base : LND_Test_Base
    {
        protected DeorbitAutopilot DEO;
        protected double latSpread;

        protected DEO_Test_Base()
        {
            program = Autopilot2.Deorbit;
        }

        protected override bool GetModule()
        {
            DEO = TCA.GetModule<DeorbitAutopilot>();
            return DEO != null;
        }

        protected override void CreateTarget(Random RND)
        {
            Coordinates c =  null;
            while(c == null || c.OnWater)
            {
                c = Coordinates.SurfacePoint(RND.NextDouble()*latSpread-latSpread/2,
                                             RND.NextDouble()*360, 
                                             VSL.Body);
                c.SetAlt2Surface(VSL.Body);
            }
            VSL.SetTarget(DEO, new WayPoint(c));
        }

        protected override void OnLand()
        {
            DEO.UseBrakes = DEO.UseChutes = true;
            if(!TrajectoryCalculator.setp_by_step_computation)
            {
                if(DEO.stage == DeorbitAutopilot.Stage.Wait)
                    TCAGui.ClearStatus();
                if(TimeWarp.CurrentRateIndex == 0 && 
                   VSL.OnPlanetParams.ParachutesActive && VSL.vessel.srfSpeed < 50 && VSL.Altitude > 100)
                    TimeWarp.SetRate(3, false, false);
                else if(TimeWarp.CurrentRateIndex > 0 && VSL.Altitude < 100)
                    TimeWarp.SetRate(0, false);
                if(DEO.landing_stage == LandingTrajectoryAutopilot.LandingStage.None &&
                   (DEO.stage < DeorbitAutopilot.Stage.Correct || DEO.stage == DeorbitAutopilot.Stage.Wait))
                {
                    VSL.Info.AddCustopWaypoint(new Coordinates(0,0,0),   "Zero");
                    VSL.Info.AddCustopWaypoint(new Coordinates(90,0,0),  "North");
                    VSL.Info.AddCustopWaypoint(new Coordinates(-90,0,0), "South");
                    VSL.Info.AddCustopWaypoint(new Coordinates(0,90,0),  "90 deg");
                    VSL.Info.AddCustopWaypoint(new Coordinates(0,180,0), "180 deg");
                    VSL.Info.AddCustopWaypoint(new Coordinates(0,270,0), "270 deg");
                    if(!MapView.MapIsEnabled)
                        MapView.EnterMapView();
                }
                else
                {
                    if(MapView.MapIsEnabled)
                    {
                        delay.Restart();
                        MapView.ExitMapView();
                    }
                    if(delay.TimePassed && CFG.Target != null)
                    {
                        var target = CFG.Target.GetTransform();
                        FlightCameraOverride.Target(FlightCameraOverride.Mode.LookFromTo, 
                                                    VSL.vessel.vesselTransform, target, 10, 
                                                    target != FlightCameraOverride.target);
                    }
                }
            }
        }
    }

    public class DEO_Test_Eve : DEO_Test_Base
    {
        public DEO_Test_Eve()
        {
            Save = "DEO Eve";
            latSpread = 2;
        }
    }

    public class DEO_Test_Duna : DEO_Test_Base
    {
        public DEO_Test_Duna()
        {
            Save = "Duna Chute Landing";
            latSpread = 60;
        }
    }


    public class DEO_Test_Kerbin : DEO_Test_Base
    {
        public DEO_Test_Kerbin()
        {
            Save = "Kerbin Chute Landing";
            latSpread = 20;
        }
    }

    public class DEO_Test_Mun : DEO_Test_Base
    {
        public DEO_Test_Mun()
        {
            Save = "DEO Mun";
            latSpread = 180;
        }
    }
}

