//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2015 Allis Tauri
//
// This work is licensed under the Creative Commons Attribution-ShareAlike 4.0 International License. 
// To view a copy of this license, visit http://creativecommons.org/licenses/by-sa/4.0/ 
// or send a letter to Creative Commons, PO Box 1866, Mountain View, CA 94042, USA.

using System;
using UnityEngine;
using AT_Utils;

namespace ThrottleControlledAvionics
{
	[CareerPart]
	[RequireModules(typeof(VerticalSpeedControl))]
	[ModuleInputs(typeof(Radar))]
	public class AltitudeControl : TCAModule
	{
		public class Config : ModuleConfig
		{
			[Persistent] public float MaxSpeedErrorF = 100f;
			[Persistent] public float MaxSpeedLow    = 10f; 
			[Persistent] public float MaxSpeedHigh   = 300f;
			[Persistent] public float ErrF           = 1f;   //altitude error coefficient
			[Persistent] public float TWRd           = 2f;   //twr denominator

			[Persistent] public float RelAltitudeFactor = 50;
			[Persistent] public float RelVelocityErrF   = 1;

			[Persistent] public float FallingTime       = 1f;
			[Persistent] public float TimeAhead         = 5f;

			[Persistent] public PIDf_Controller2 RocketPID = new PIDf_Controller2(0.1f, 0.5f, 0.03f, -9.9f, -9.9f);
			[Persistent] public PIDf_Controller JetsPID   = new PIDf_Controller(0.5f, 0, 0.5f, -9.9f, -9.9f);

			public override void Init()
			{
				RocketPID.Max = JetsPID.Max =  MaxSpeedLow;
				RocketPID.Min = JetsPID.Min = -MaxSpeedLow;
			}
		}
		static Config ALT { get { return Globals.Instance.ALT; } }

		static readonly ActionDamper UpDamper = new ActionDamper();
		static readonly ActionDamper DownDamper = new ActionDamper();

		readonly PIDf_Controller2 rocket_pid = new PIDf_Controller2();
		readonly PIDf_Controller  jets_pid   = new PIDf_Controller();
		readonly Timer            Falling    = new Timer();
		AtmoSim sim;

		Radar RAD;

		public AltitudeControl(ModuleTCA tca) : base(tca) 
		{
			UpDamper.Period = GLB.KeyRepeatTime;
			DownDamper.Period = GLB.KeyRepeatTime;
		}

		public override void Init()
		{
			base.Init();
			rocket_pid.setPID(ALT.RocketPID);
			jets_pid.setPID(ALT.JetsPID);
			Falling.Period = ALT.FallingTime;
			CFG.VF.AddHandler(this, VFlight.AltitudeControl);
			if(VSL.LandedOrSplashed) CFG.DesiredAltitude = -10;
			sim = new AtmoSim(VSL);
		}

		protected override void UpdateState()
		{ 
			base.UpdateState();
			IsActive &= VSL.OnPlanet && CFG.VF[VFlight.AltitudeControl]; 
		}

		public void SetAltitudeAboveTerrain(bool enable = true)
		{
			if(RAD == null) return;
			CFG.AltitudeAboveTerrain = enable;
			VSL.Altitude.Update();
			Falling.Reset();
			if(CFG.AltitudeAboveTerrain)
				CFG.DesiredAltitude -= VSL.Altitude.TerrainAltitude;
			else CFG.DesiredAltitude += VSL.Altitude.TerrainAltitude;
		}

		public void AltitudeControlCallback(Multiplexer.Command cmd)
		{
			Falling.Reset();
			switch(cmd)
			{
			case Multiplexer.Command.Resume:
				NeedRadarWhenMooving();
				break;

			case Multiplexer.Command.On:
				VSL.Altitude.Update();
				CFG.DesiredAltitude = VSL.LandedOrSplashed? -10f : VSL.Altitude.Relative;
				if(!CFG.AltitudeAboveTerrain) CFG.DesiredAltitude += VSL.Altitude.TerrainAltitude;
				goto case Multiplexer.Command.Resume;

			case Multiplexer.Command.Off:
				UnregisterFrom<Radar>();
				break;
			}
		}

		void set_ascending_state()
		{ if(VSL.VerticalSpeed.Absolute > 1 && CFG.VerticalCutoff > 1) SetState(TCAState.Ascending); }

		protected override void Update()
		{
			if(!IsActive) return;
			SetState(TCAState.AltitudeControl);
			//calculate current altitude or apoapsis, if the vessel is moving upwards
			var alt = VSL.Altitude.Absolute;
			var ttAp = 0f;
			if(VSL.VerticalSpeed.Absolute > 0 && !VSL.LandedOrSplashed)
			{
				if(RAD != null && RAD.TimeAhead > 0 && RAD.TimeAhead < ttAp) ttAp = RAD.TimeAhead;
				else ttAp = VSL.VerticalSpeed.Absolute/VSL.Physics.G;
				alt = VSL.Altitude.Absolute+ttAp*(VSL.VerticalSpeed.Absolute - ttAp*VSL.Physics.G/2);
//				CSV(CFG.DesiredAltitude, alt-VSL.Altitude.TerrainAltitude, VSL.Altitude.Relative, RAD.AltitudeAhead-VSL.Altitude.Absolute,
//				    VSL.VSF, -VSL.Physics.G, ttAp, RAD.TimeAhead);//debug
			}
			//correct for terrain altitude and radar data if following terrain
			if(CFG.AltitudeAboveTerrain) 
			{
				var obstacle_ahead = VSL.HorizontalSpeed.MoovingFast && alt-VSL.Altitude.Ahead <= VSL.Geometry.H;
				if(obstacle_ahead) 
				{
					var dAlt = VSL.Altitude.Ahead+CFG.DesiredAltitude-VSL.Altitude.Absolute;
					SetState(VSL.VerticalSpeed.Absolute < 0? TCAState.GroundCollision : TCAState.ObstacleAhead);
					if(VSL.Altitude.CorrectionAllowed && RAD != null && 
					   RAD.TimeAhead > 0 && dAlt/RAD.TimeAhead > GLB.VSC.MaxSpeed)
					{
						
//						var G_A  = Utils.ClampL(VSL.Physics.G*(1-VSL.OnPlanetParams.DTWR), 1e-5f);
//						CFG.VerticalCutoff = Mathf.Sqrt(2*Utils.ClampL(dAlt * G_A, 0));
//						ttAp = CFG.VerticalCutoff/G_A;
//						if(ttAp > RAD.TimeAhead)
						CFG.VerticalCutoff = dAlt/Utils.ClampL(RAD.TimeAhead-GLB.CPS.LookAheadTime, 1e-5f);
						if(VSL.Engines.Slow)
						{
							var dV = CFG.VerticalCutoff - VSL.VerticalSpeed.Absolute;
							dV *= dV < 0 ? VSL.Engines.DecelerationTime : VSL.Engines.AccelerationTime;
							CFG.VerticalCutoff += dV;
						}
//						Log("VSF {}, vV {}, hV {}, G_A {}, dAlt {}, ttAp {}, TimeAhead {}", 
//						    CFG.VerticalCutoff, VSL.VerticalSpeed.Absolute, VSL.HorizontalSpeed.Absolute, G_A, dAlt, ttAp, RAD.TimeAhead);//debug
						return;
					}
				}
				var lower_threshold = Mathf.Max(VSL.Altitude.TerrainAltitude, VSL.Altitude.LowerThreshold);
				if(VSL.Altitude.CorrectionAllowed) lower_threshold = Mathf.Max(VSL.Altitude.Ahead, lower_threshold);
				alt -= lower_threshold;
				if(alt < 0) set_ascending_state();
			}
			//calculate altitude error
			var error = (CFG.DesiredAltitude-alt);
			//turn off the engines if landed
			if(VSL.LandedOrSplashed && error < 0 && VSL.Altitude.Relative <= VSL.Geometry.R)
			{
//				Log("nAlt {}, alt {}, error {}, VSF {}", CFG.DesiredAltitude, alt, error, CFG.VerticalCutoff);//debug
				CFG.VerticalCutoff = -GLB.VSC.MaxSpeed;
				return;
			}
			//calculate min/max speed
			var min_speed = -ALT.MaxSpeedLow;
			var max_speed =  ALT.MaxSpeedLow;
			if(error < 0)
			{
				min_speed = Utils.Clamp(ALT.MaxSpeedLow*(error+ALT.MaxSpeedErrorF)/ALT.MaxSpeedErrorF, -ALT.MaxSpeedHigh, -ALT.MaxSpeedLow);
				if(VSL.VerticalSpeed.Absolute < 0)
				{
					double terminal_velocity;
					var free_fall  = VSL.OnPlanet && VSL.Body.atmosphere? 
						(float)sim.FreeFallTime(VSL.Altitude.Relative+error, out terminal_velocity) :
						(VSL.VerticalSpeed.Absolute+Mathf.Sqrt(VSL.VerticalSpeed.Absolute*VSL.VerticalSpeed.Absolute-2*VSL.Physics.G*error))/VSL.Physics.G;
					var brake_time = -VSL.VerticalSpeed.Absolute/(VSL.OnPlanetParams.MaxTWR-1)/VSL.Physics.G;
					if(brake_time < 0) min_speed = 0;
					else if(brake_time > free_fall/100) 
						min_speed = Utils.Clamp(-ALT.MaxSpeedHigh*(1-brake_time/free_fall), min_speed, 
						                        Utils.ClampL(free_fall*VSL.Physics.G*(1-VSL.OnPlanetParams.MaxTWR), -ALT.MaxSpeedLow));
//					Log("error {}, vV {}, free_fall {}, brake_time {}, min_speed {}", 
//					    -error, -VSL.VerticalSpeed.Absolute, free_fall, brake_time, min_speed);//debug
				}
			}
			else if(error > 0) max_speed = alt <= VSL.Geometry.H? ALT.MaxSpeedHigh :
				Utils.Clamp(ALT.MaxSpeedLow*(error-ALT.MaxSpeedErrorF)/ALT.MaxSpeedErrorF, ALT.MaxSpeedLow, ALT.MaxSpeedHigh);
			//update pid parameters and vertical speed setpoint
			if(VSL.Engines.Slow)
			{
				jets_pid.Min = min_speed;
				jets_pid.Max = max_speed; 
				jets_pid.P = Utils.ClampH(ALT.JetsPID.P/VSL.OnPlanetParams.MaxTWR/ALT.TWRd*
				                          Mathf.Clamp(Mathf.Abs(1/VSL.VerticalSpeed.Absolute)*ALT.ErrF, 1, VSL.OnPlanetParams.MaxTWR*ALT.TWRd), 
				                          ALT.JetsPID.P);
				jets_pid.D = ALT.JetsPID.D;
				if(CFG.AltitudeAboveTerrain)
					jets_pid.D /= Utils.ClampL(VSL.HorizontalSpeed, 1);
				jets_pid.D *= error < 0? 1+VSL.Engines.AccelerationTime : 1+VSL.Engines.DecelerationTime;
				jets_pid.Update(error);
				CFG.VerticalCutoff = jets_pid.Action;
//				Log("nAlt {}, alt {}, error {}, hV {}, VSF {}\njPID: {}", 
//				    CFG.DesiredAltitude, alt, error, VSL.HorizontalSpeed, CFG.VerticalCutoff, jets_pid);//debug
			}
			else 
			{
				rocket_pid.Min = min_speed;
				rocket_pid.Max = max_speed;
				rocket_pid.D = ALT.RocketPID.D;
				if(CFG.AltitudeAboveTerrain)
					rocket_pid.D /= Utils.ClampL(VSL.HorizontalSpeed, 1);
				rocket_pid.Update(error);
				CFG.VerticalCutoff = rocket_pid.Action;
//				Log("nAlt {}, alt {}, error {}, hV {}, VSF {}\nrPID: {}", 
//				    CFG.DesiredAltitude, alt, error, VSL.HorizontalSpeed, CFG.VerticalCutoff, rocket_pid);//debug
			}
			//correct for relative vertical speed
			//if following the terrain and flying below desired altitude
			if(CFG.AltitudeAboveTerrain)
			{
				var dV = 0f;
				if(error > 0) 
					dV = Utils.ClampL((VSL.VerticalSpeed.Absolute-VSL.VerticalSpeed.Relative)/
					                  Utils.ClampL(alt/ALT.RelAltitudeFactor, 1), 0);
				CFG.VerticalCutoff += dV;
				//Loosing Altitude alert
				Falling.RunIf(() => SetState(TCAState.LoosingAltitude),
				              VSL.VerticalSpeed.Relative < 0 && 
				              VSL.CFG.VerticalCutoff-VSL.VerticalSpeed.Absolute > 0 && 
				              VSL.Altitude < CFG.DesiredAltitude-VSL.VerticalSpeed.Relative*ALT.TimeAhead);
//				Log("error {0}, dV: {1}, VSP: {2}, min speed {3}, max speed {4}", 
//				    error, dV, CFG.VerticalCutoff, min_speed, max_speed);//debug
//				CSV(alt, VSL.vessel.altitude, VSL.Altitude.TerrainAltitude, VSL.Altitude, RAD.AltitudeAhead, error, 
//				    CFG.VerticalCutoff, VSL.VSF, VSL.MinVSF, VSL.VerticalSpeed, VSL.VerticalSpeed.Relative, 
//				    dV, min_speed, max_speed);//debug
			}
		}

		public override void ProcessKeys()
		{
			update_altitude();
			if(GameSettings.THROTTLE_UP.GetKey())
				altitude = Mathf.Lerp(CFG.DesiredAltitude, 
				                      CFG.DesiredAltitude+10, 
				                      CFG.ControlSensitivity);
			else if(GameSettings.THROTTLE_DOWN.GetKey())
				altitude = Mathf.Lerp(CFG.DesiredAltitude,
				                      CFG.DesiredAltitude-10, 
				                      CFG.ControlSensitivity);
			else if(GameSettings.THROTTLE_FULL.GetKey())
				UpDamper.Run(() => altitude = altitude+10);
			else if(GameSettings.THROTTLE_CUTOFF.GetKey())
				DownDamper.Run(() => altitude = altitude-10);
			if(!altitude.Equals(CFG.DesiredAltitude)) set_altitude();
		}

		#region GUI
		float altitude;
		string s_altitude = null;

		void update_altitude()
		{
			if(s_altitude == null || !altitude.Equals(CFG.DesiredAltitude))
				s_altitude = CFG.DesiredAltitude.ToString("F1");
			altitude = CFG.DesiredAltitude;
		}

		void set_altitude(VesselConfig cfg)
		{ cfg.DesiredAltitude = altitude; cfg.BlockThrottle = true; }

		void set_altitude()
		{
			TCA.SquadConfigAction(set_altitude);
			s_altitude = altitude.ToString("F1");
		}

		public override void Draw()
		{
			update_altitude();
			var above_ground = VSL.Altitude.AboveGround;
			var style = above_ground? Styles.green : Styles.red;
			GUILayout.Label(new GUIContent("Alt. (m):", above_ground? 
			                               "Desired altitude is above the ground" : 
			                               "Warning! Desired altitude is below the ground"), 
			                Styles.white, GUILayout.ExpandWidth(true));
			s_altitude = GUILayout.TextField(s_altitude, style, GUILayout.ExpandWidth(true), GUILayout.MinWidth(60));
			if(GUILayout.Button("Set", Styles.normal_button, GUILayout.ExpandWidth(false))) 
			{
				if(float.TryParse(s_altitude, out altitude)) set_altitude();
				else altitude = CFG.DesiredAltitude;
			}
			if(GUILayout.Button("-10m", Styles.normal_button, GUILayout.ExpandWidth(false))) 
			{ altitude -= 10; set_altitude(); }
			if(GUILayout.Button("+10m", Styles.normal_button, GUILayout.ExpandWidth(false))) 
			{ altitude += 10; set_altitude(); }
		}
		#endregion
	}
}

