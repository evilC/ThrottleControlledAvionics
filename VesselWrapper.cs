﻿//   VesselWrapper.cs
//
//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2015 Allis Tauri
//
// This work is licensed under the Creative Commons Attribution 4.0 International License. 
// To view a copy of this license, visit http://creativecommons.org/licenses/by/4.0/ 
// or send a letter to Creative Commons, PO Box 1866, Mountain View, CA 94042, USA.

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace ThrottleControlledAvionics
{
	public class VesselWrapper
	{
		const float Gee = 9.82f;

		const string ElectricChargeName = "ElectricCharge";
		static PartResourceDefinition _electric_charge;
		public static PartResourceDefinition ElectricCharge
		{ 
			get
			{ 
				if(_electric_charge == null)
					_electric_charge = PartResourceLibrary.Instance.GetDefinition(ElectricChargeName);
				return _electric_charge;
			} 
		}

		public Vessel vessel { get; private set; }
		public Transform refT { get; private set; } //transform of the controller-part
		public VesselConfig CFG { get; private set; }
		public TCAGlobals GLB { get { return TCAScenario.Globals; } }

		public List<EngineWrapper> Engines         = new List<EngineWrapper>();
		public List<EngineWrapper> ActiveEngines   = new List<EngineWrapper>();
		public List<EngineWrapper> BalancedEngines = new List<EngineWrapper>();
		public List<EngineWrapper> ManeuverEngines = new List<EngineWrapper>();
		public List<EngineWrapper> SteeringEngines = new List<EngineWrapper>();
		public List<EngineWrapper> ManualEngines = new List<EngineWrapper>();
		public List<ModuleReactionWheel> RWheels = new List<ModuleReactionWheel>();
		public List<RCSWrapper> RCS = new List<RCSWrapper>();
		public List<RCSWrapper> ActiveRCS = new List<RCSWrapper>();

		public int  NumActive { get; private set; }
		public int  NumActiveRCS { get; private set; }
		public bool NormalizeLimits = true; //if engines' limits should be normalized

		//physics
		public Vector6 E_TorqueLimits { get; private set; } = new Vector6(); //torque limits of engines
		public Vector6 W_TorqueLimits { get; private set; } = new Vector6(); //torque limits of reaction wheels
		public Vector6 R_TorqueLimits { get; private set; } = new Vector6(); //torque limits of rcs

		public Bounds  B { get; private set; } //bounds
		public Vector3 C { get; private set; } //center
		public float   H { get; private set; } //height
		public float   R { get; private set; } //radius
		public float   M { get; private set; } //mass
		public float   G { get { return (float)vessel.mainBody.GeeASL*Gee; } }
		public float  DTWR { get; private set; }
		public float  MaxDTWR { get; private set; }
		public float  MaxTWR { get; private set; }
		public float  AccelSpeed { get; private set; }
		public float  DecelSpeed { get; private set; }
		public bool   SlowEngines { get; private set; }
		public float  VSF; //vertical speed factor
		public float  MinVSF;
		public float  MinVSFtwr;

		public Vector3d   Up { get; private set; }  //up unit vector in world space
		public Vector3    Fwd { get; private set; }  //fwd unit vector of the Control module in world space
		public bool       NoseUp { get; private set; }  //if the forward is refT.forward or refT.up
		public Vector3    CoM { get { return vessel.CoM + vessel.rb_velocity*TimeWarp.fixedDeltaTime; } } //current center of mass of unpacked vessel
		public Vector3    wCoM { get; private set; } //center of mass in world space
		public Vector3    MoI { get; private set; } = Vector3.one; //main diagonal of inertia tensor
		public Matrix3x3f InertiaTensor { get; private set; }
		public Vector3    MaxAngularA { get; private set; } //current maximum angular acceleration
		public float      MaxAngularA_m { get; private set; } //current maximum angular acceleration
		public float      MaxPitchRollAA { get; private set; } //current minimal maximum angular acceleration

		public Vector3  Thrust { get; private set; } //current total thrust
		public Vector3  MaxThrust { get; private set; }
		public Vector3  Torque { get; private set; } //current torque applied to the vessel by the engines
		public Vector3  MaxTorque { get; private set; }
		public float    AbsVerticalSpeed { get; private set; }
		public float    RelVerticalSpeed { get; private set; }
		public float    VerticalSpeed { get; private set; }
		public float    VerticalSpeedDisp { get; private set; }
		public float    VerticalAccel { get; private set; }
		public float    Altitude { get; private set; }
		public float    AltitudeAhead;
		public float    TerrainAltitude { get; private set; }
		public Vector3d HorizontalVelocity { get; private set; }
		public float    HorizontalSpeed { get; private set; }

		//unlike the vessel.verticalSpeed, this method is unaffected by ship's rotation (from MechJeb)
		float CoM_verticalSpeed { get { return (float)Vector3d.Dot(vessel.srf_velocity, Up); } }

		public TCAState State;
		public void SetState(TCAState state) { State |= state; }
		public bool IsStateSet(TCAState state) { return (State & state) == state; }
		public bool ElectricChargeAvailible
		{
			get
			{
				var ec = vessel.GetActiveResource(ElectricCharge);
				return ec != null && ec.amount > 0;
			}
		}

		public CelestialBody mainBody { get { return vessel.mainBody; } }
		public bool OnPlanet { get; private set; }
		public bool isEVA { get { return vessel.isEVA; } }
		public bool LandedOrSplashed { get { return vessel.LandedOrSplashed; } }
		public ActionGroupList ActionGroups { get { return vessel.ActionGroups; } }
		public FlightCtrlState ctrlState { get { return vessel.ctrlState; } }
		public FlightInputCallback OnAutopilotUpdate 
		{ get { return vessel.OnAutopilotUpdate; } set { vessel.OnAutopilotUpdate = value; } }
		public Vector3 Steering { get; private set; }
		public Vector3 Translation { get; private set; }
		public bool NoActiveRCS { get; private set; }
		public bool HasTarget { get { return vessel.targetObject != null && !(vessel.targetObject is CelestialBody); } }

		public VesselWrapper(Vessel vsl, VesselConfig cfg) 
		{
			vessel = vsl; CFG = cfg;
//			if(CFG == null) 
//				CFG = TCAScenario.GetConfig(this);
		}

		public void Init() 
		{
			AltitudeAhead = -1;
			OnPlanet = _OnPlanet();
		}

		bool _OnPlanet() 
		{ 
			return (vessel.situation != Vessel.Situations.DOCKED   &&
			        vessel.situation != Vessel.Situations.ORBITING &&
			        vessel.situation != Vessel.Situations.ESCAPING); 
		}

		public Vector3 GetStarboard(Vector3d hV) { return Quaternion.FromToRotation(Up, Vector3.up)*Vector3d.Cross(hV, Up); }
		public Vector3 CurrentStarboard { get { return Quaternion.FromToRotation(Up, Vector3.up)*Vector3d.Cross(HorizontalVelocity, Up); } }

		#region Engines
		public void UpdateEngines()
		{
			EngineWrapper.ThrustPI.setMaster(CFG.Engines);
			Engines.Clear();
			foreach(Part p in vessel.Parts)
				foreach(var module in p.Modules)
				{	
					var engine = module as ModuleEngines;
					if(engine != null)
					{ 
						Engines.Add(new EngineWrapper(engine)); 
						continue; 
					}

					var rwheel = module as ModuleReactionWheel;
					if(rwheel != null) { RWheels.Add(rwheel); continue; }

					var rcs = module as ModuleRCS;
					if(rcs != null) { RCS.Add(new RCSWrapper(rcs)); continue; }
				}
			if(CFG.EnginesProfiles.Empty) CFG.EnginesProfiles.AddProfile(Engines);
			else CFG.ActiveProfile.Update(Engines);
		}

		public bool CheckEngines()
		{
			//update engines' list if needed
			bool update = false;
			var num_engines = Engines.Count;
			for(int i = 0; i < num_engines; i++)
			{ update |= !Engines[i].Valid; if(update) break; }
			if(!update)
			{
				for(int i = 0; i < RCS.Count; i++)
				{ update |= !RCS[i].Valid; if(update) break; }
			}
			if(update) UpdateEngines();
			//unflameout engines
			for(int i = 0; i < num_engines; i++)
			{ var e = Engines[i]; if(e.engine.flameout) e.forceThrustPercentage(1); }
			//sync with active profile
			if(CFG.ActiveProfile.Changed) CFG.ActiveProfile.Apply(Engines);
			else CFG.ActiveProfile.Update(Engines);
			//get active engines
			ActiveEngines.Clear(); ActiveEngines.Capacity = Engines.Count;
			for(int i = 0; i < num_engines; i++)
			{ 
				var e = Engines[i]; 
				if(e.isOperational) ActiveEngines.Add(e); 
			}
			ActiveRCS = vessel.ActionGroups[KSPActionGroup.RCS]? 
				RCS.Where(t => t.isOperational).ToList() : new List<RCSWrapper>();
			NumActive = ActiveEngines.Count;
			NumActiveRCS = ActiveRCS.Count;
			NoActiveRCS = NumActiveRCS == 0 || 
				Steering.sqrMagnitude < GLB.InputDeadZone && 
				Translation.sqrMagnitude < GLB.InputDeadZone;
			return NumActive > 0 && vessel.ctrlState.mainThrottle > 0 || !NoActiveRCS;
		}

		public void SortEngines()
		{
			SteeringEngines.Clear(); SteeringEngines.Capacity = NumActive;
			ManeuverEngines.Clear(); ManeuverEngines.Capacity = NumActive;
			BalancedEngines.Clear(); BalancedEngines.Capacity = NumActive;
			ManualEngines.Clear();   ManualEngines.Capacity   = NumActive;
			for(int i = 0; i < NumActive; i++)
			{
				var e = ActiveEngines[i];
				switch(e.Role)
				{
				case TCARole.MAIN:
					SteeringEngines.Add(e);
					break;
				case TCARole.MANEUVER:
					SteeringEngines.Add(e);
					ManeuverEngines.Add(e);
					break;
				case TCARole.BALANCE:
					BalancedEngines.Add(e);
					break;
				case TCARole.MANUAL:
					ManualEngines.Add(e);
					break;
				}
			}
			NormalizeLimits = SteeringEngines.Count > ManeuverEngines.Count;
		}

		public void TuneEngines()
		{
			NormalizeLimits = true;
			//calculate VSF correction
			if(IsStateSet(TCAState.VerticalSpeedControl))
			{
				//calculate min imbalance
				var min_imbalance = Vector3.zero;
				for(int i = 0; i < NumActive; i++) min_imbalance += ActiveEngines[i].Torque(0);
				//correct VerticalSpeedFactor if needed
				if(!min_imbalance.IsZero())
				{
					var anti_min_imbalance = Vector3.zero;
					for(int i = 0; i < NumActive; i++)
					{
						var e = ActiveEngines[i];
						if(Vector3.Dot(e.specificTorque, min_imbalance) < 0)
							anti_min_imbalance += e.specificTorque * e.nominalCurrentThrust(1);
					}
					anti_min_imbalance = Vector3.Project(anti_min_imbalance, min_imbalance);
					VSF = Mathf.Clamp(VSF, Mathf.Clamp01(min_imbalance.magnitude/anti_min_imbalance.magnitude
					                                     *GLB.VSC.BalanceCorrection), 1f);
				}
				for(int i = 0; i < NumActive; i++)
				{
					var e = ActiveEngines[i];
					if(e.isVSC)
					{
						e.VSF = e.VSF > 0 ? VSF : MinVSF;
						e.throttle = e.VSF * vessel.ctrlState.mainThrottle;
					}
					else 
					{
						e.throttle = vessel.ctrlState.mainThrottle;
						e.VSF = 1f;
					}
					e.currentTorque = e.Torque(e.throttle);
					e.currentTorque_m = e.currentTorque.magnitude;
				}
			}
			else
			{
				for(int i = 0; i < NumActive; i++)
				{
					var e = ActiveEngines[i];
					e.VSF = 1f;
					e.throttle = vessel.ctrlState.mainThrottle;
					e.currentTorque = e.Torque(e.throttle);
					e.currentTorque_m = e.currentTorque.magnitude;
				}
			}
		}

		public void SetThrustLimiters()
		{
			for(int i = 0; i < NumActive; i++)
			{
				var e = ActiveEngines[i];
				if(e.Role != TCARole.MANUAL) e.thrustLimit = Mathf.Clamp01(e.VSF * e.limit);
			}
			if(NoActiveRCS) return;
			for(int i = 0; i < NumActiveRCS; i++)
			{
				var t = ActiveRCS[i];
				t.thrustLimit = Mathf.Clamp01(t.limit);
			}
		}
		#endregion

		#region Updates
		public void UpdateAltitude()
		{ 
			if(CFG.AltitudeAboveTerrain)
			{
				TerrainAltitude = (float)((vessel.mainBody.ocean && vessel.terrainAltitude < 0)? 0 : vessel.terrainAltitude);
				Altitude = (float)(vessel.altitude) - TerrainAltitude;
			}
			else Altitude = (float)vessel.altitude;
		}

		public void UpdateState()
		{
			var on_planet = _OnPlanet();
			if(on_planet != OnPlanet) CFG.EnginesProfiles.OnPlanetChanged(on_planet);
			OnPlanet = on_planet;
			Steering = new Vector3(vessel.ctrlState.pitch, vessel.ctrlState.roll, vessel.ctrlState.yaw);
			Translation = new Vector3(vessel.ctrlState.X, vessel.ctrlState.Z, vessel.ctrlState.Y);
			if(!Steering.IsZero()) Steering = Steering/Steering.CubeNorm().magnitude;
			if(!Translation.IsZero())Translation = Translation/Translation.CubeNorm().magnitude;
			if(!OnPlanet) UnblockSAS(false);
			else if(!CFG.HF) UnblockSAS();
		}

		public void UpdateCommons()
		{
			wCoM = vessel.CurrentCoM;
			refT = vessel.ReferenceTransform;
			Up   = (wCoM - vessel.mainBody.position).normalized; //duplicates vessel.upAxis, except it uses CoM instead of CurrentCoM
			//init engine wrappers
			for(int i = 0; i < NumActive; i++) 
			{
				var e = ActiveEngines[i];
				e.InitState();
				e.thrustDirection = refT.InverseTransformDirection(e.wThrustDir);
				e.wThrustLever = e.wThrustPos-wCoM;
				e.specificTorque = refT.InverseTransformDirection(Vector3.Cross(e.wThrustLever, e.wThrustDir));
				e.torqueRatio = Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(Vector3.Dot(e.wThrustLever.normalized, e.wThrustDir))), 
				                          GLB.ENG.TorqueRatioFactor);
			}
			//init RCS wrappers if needed
			if(!NoActiveRCS)
			{
				for(int i = 0; i < NumActiveRCS; i++)
				{
					var t = ActiveRCS[i];
					t.InitState();
					t.thrustDirection = refT.InverseTransformDirection(t.wThrustDir);
					t.wThrustLever = t.wThrustPos-wCoM;
					t.specificTorque = refT.InverseTransformDirection(Vector3.Cross(t.wThrustLever, t.wThrustDir));
					t.torqueRatio = Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(Vector3.Dot(t.wThrustLever.normalized, t.wThrustDir))), GLB.RCS.TorqueRatioFactor);
					t.currentTorque = t.Torque(1);
					t.currentTorque_m = t.currentTorque.magnitude;
				}
			}
			UpdateETorqueLimits();
			UpdateRTorqueLimits();
			UpdateWTorqueLimits();
			update_MaxAngularA();
		}

		public void UpdateETorqueLimits()
		{
			E_TorqueLimits = new Vector6();
			for(int i = 0; i < SteeringEngines.Count; i++)
				E_TorqueLimits.Add(SteeringEngines[i].currentTorque);
		}

		public void UpdateWTorqueLimits()
		{
			W_TorqueLimits = new Vector6();
			for(int i = 0; i < RWheels.Count; i++)
			{
				var w = RWheels[i];
				if(!w.operational) continue;
				W_TorqueLimits.Add(refT.InverseTransformDirection(new Vector3(w.PitchTorque, w.RollTorque, w.YawTorque)));
			}
		}

		public void UpdateRTorqueLimits()
		{
			R_TorqueLimits = new Vector6();
			for(int i = 0; i < RCS.Count; i++)
			{
				var r = RCS[i];
				if(!r.rcs.isEnabled) continue;
				for(int j = 0; j < r.rcs.thrusterTransforms.Count; j++)
				{
					var t = r.rcs.thrusterTransforms[j];
					if(t == null) continue;
					R_TorqueLimits.Add(refT.InverseTransformDirection(Vector3.Cross(t.position-wCoM, t.up)*r.nominalThrusterPower));
				}
			}
		}

		public void UpdateTorque(params IList<EngineWrapper>[] engines)
		{
			Torque = Vector3.zero;
			for(int i = 0; i < engines.Length; i++)
			{
				for(int j = 0; j < engines[i].Count; j++)
				{
					var e = engines[i][j];
					Torque += e.Torque(e.throttle * e.limit);
				}
			}
		}

		public void UpdateOnPlanetStats()
		{
			if(!OnPlanet) return;
			AccelSpeed = 0f; DecelSpeed = 0f; SlowEngines = false;
			//calculate altitude, vertical and horizontal speed and acceleration
			AbsVerticalSpeed  = CoM_verticalSpeed;
			VerticalAccel     = (AbsVerticalSpeed-VerticalSpeed)/TimeWarp.fixedDeltaTime;
			VerticalSpeed     = AbsVerticalSpeed;
			VerticalSpeedDisp = AbsVerticalSpeed;
			var old_alt = Altitude;
			UpdateAltitude();
			//use relative vertical speed instead of absolute if following terrain
			if(CFG.AltitudeAboveTerrain)
			{
				RelVerticalSpeed  = (Altitude - old_alt)/TimeWarp.fixedDeltaTime;
				VerticalSpeedDisp = RelVerticalSpeed;
			}
			HorizontalVelocity = Vector3d.Exclude(Up, vessel.srf_velocity);
			HorizontalSpeed = (float)HorizontalVelocity.magnitude;
			//calculate total downward thrust and slow engines' corrections
			Thrust = Vector3.zero;
			MaxThrust = Vector3.zero;
			var down_thrust = 0f;
			var slow_thrust = 0f;
			var fast_thrust = 0f;
			for(int i = 0; i < NumActive; i++)
			{
				var e = ActiveEngines[i];
				e.VSF = 1f;
				if(e.thrustInfo == null) continue;
				if(e.isVSC)
				{
					var dcomponent = -Vector3.Dot(e.wThrustDir, Up);
					if(dcomponent <= 0) e.VSF = 0;
					else 
					{
						var dthrust = e.nominalCurrentThrust(e.best_limit)*dcomponent;
						if(e.useEngineResponseTime && dthrust > 0) 
						{
							slow_thrust += dthrust;
							AccelSpeed += e.engineAccelerationSpeed*dthrust;
							DecelSpeed += e.engineDecelerationSpeed*dthrust;
						}
						else fast_thrust = dthrust;
						down_thrust += dthrust;
						MaxThrust += e.wThrustDir*e.nominalCurrentThrust(1);
					}
				}
				//do not include maneuver engines to break the feedback loop with HSC
				if(e.Role != TCARole.MANEUVER) 
					Thrust += e.wThrustDir*e.finalThrust;
			}
			M = vessel.GetTotalMass();
			MaxTWR  = MaxThrust.magnitude/M/G;
			MaxDTWR = Utils.EWA(MaxDTWR, down_thrust/M/G, 0.1f);
			DTWR = Vector3.Dot(Thrust, Up) < 0? Vector3.Project(Thrust, Up).magnitude/M/G : 0f;
			MaxPitchRollAA = Vector3.ProjectOnPlane(MaxAngularA, refT.InverseTransformDirection(Thrust)).magnitude;
			if(refT != null)
			{
				Fwd  = Vector3.Cross(refT.right, -MaxThrust).normalized;
				NoseUp = Vector3.Dot(Fwd, refT.forward) >= 0.9;
			}
			MinVSFtwr = 1/Utils.ClampL(MaxTWR, 1);
			var mVSFtor = (MaxPitchRollAA > 0)? Utils.ClampH(GLB.VSC.MinVSFf/MaxPitchRollAA, 0.9f*MinVSFtwr) : 0.1f*MinVSFtwr;
			MinVSF = Mathf.Lerp(0.1f*MinVSFtwr, mVSFtor, Mathf.Pow(Steering.sqrMagnitude, 0.25f));
//			Utils.Log("MaxAA: {0}\nThrust: {1}\nMaxPRAA {2}", MaxAngularA, refT.InverseTransformDirection(Thrust), MaxPitchRollAA);//debug
//			Utils.Log("MaxTWR {0}, G {1}, MaxPitchYawAA {2}, mVSFtwr {3}, mVSFtor {4}, MinVSF {5}", 
//			          MaxTWR, G, MaxPitchRollAA, MinVSFtwr, mVSFtor, MinVSF);//debug
			var controllable_thrust = slow_thrust+fast_thrust;
			if(controllable_thrust.Equals(0)) return;
			//correct setpoint for current TWR and slow engines
			if(AccelSpeed > 0) AccelSpeed = controllable_thrust/AccelSpeed*GLB.VSC.ASf;
			if(DecelSpeed > 0) DecelSpeed = controllable_thrust/DecelSpeed*GLB.VSC.DSf;
			SlowEngines = AccelSpeed > 0 || DecelSpeed > 0;
		}

		public void UpdateBounds()
		{
			var vT = vessel.vesselTransform;
			var b = new Bounds();
			bool inited = false;
			var parts = vessel.parts;
			for(int i = 0, partsCount = parts.Count; i < partsCount; i++)
			{
				Part p = parts[i];
				if(p == null) continue;
				foreach(var m in p.FindModelComponents<MeshFilter>())
				{
					//skip meshes without renderer
					if(m.renderer == null || !m.renderer.enabled) continue;
					var bounds = Utils.BoundCorners(m.sharedMesh.bounds);
					for(int j = 0; j < 8; j++)
					{
						var c = vT.InverseTransformPoint(m.transform.TransformPoint(bounds[j]));
						if(inited) b.Encapsulate(c);
						else
						{
							b = new Bounds(c, Vector3.zero);
							inited = true;
						}
					}
				}
			}
			B = b;
			C = vT.TransformPoint(B.center);
			H = Mathf.Abs(Vector3.Dot(vT.TransformDirection(B.extents), Up))-Vector3.Dot(C-wCoM, Up);
			R = B.extents.magnitude;
//			DebugUtils.logBounds("Vessel", B);
//			Utils.Log("B.center-wCoM {0}\n (c-wCoM)*Up {1}, B.extents*Up {2}, H {3}", 
//			          vT.TransformPoint(B.center)-wCoM, 
//			          Vector3.Dot(vT.TransformPoint(B.center)-wCoM, Up), 
//			          Mathf.Abs(Vector3.Dot(vT.TransformDirection(B.extents), Up)),
//			          H);//debug
		}

		public void UnblockSAS(bool set_flag = true)
		{
			if(CFG.SASIsControlled) 
				ActionGroups.SetGroup(KSPActionGroup.SAS, CFG.SASWasEnabled);
			if(set_flag) CFG.SASIsControlled = false;
		}

		void update_MaxAngularA()
		{
			MaxTorque = E_TorqueLimits.Max+R_TorqueLimits.Max+W_TorqueLimits.Max;
			var new_angularA = new Vector3
				(
					!MoI.x.Equals(0)? MaxTorque.x/MoI.x : float.MaxValue,
					!MoI.y.Equals(0)? MaxTorque.y/MoI.y : float.MaxValue,
					!MoI.z.Equals(0)? MaxTorque.z/MoI.z : float.MaxValue
				);
			MaxAngularA = new_angularA; //Utils.EWA(MaxAngularA, new_angularA);
			MaxAngularA_m = MaxAngularA.magnitude;
		}

		#region From MechJeb2
		// KSP's calculation of the vessel's moment of inertia is broken.
		// This function is somewhat expensive :(
		// Maybe it can be optimized more.
		static readonly Vector3[] unitVectors = { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
		public void UpdateMoI()
		{
			if(vessel == null || vessel.rigidbody == null) return;
			InertiaTensor = new Matrix3x3f();
			Transform vesselTransform = vessel.GetTransform();
			Quaternion inverseVesselRotation = Quaternion.Inverse(vesselTransform.rotation);
			foreach(Part p in vessel.parts)
			{
				var rb = p.Rigidbody;
				if (rb == null) continue;
				//Compute the contributions to the vessel inertia tensor due to the part inertia tensor
				Vector3 principalMoments = rb.inertiaTensor;
				Quaternion principalAxesRot = inverseVesselRotation * p.transform.rotation * rb.inertiaTensorRotation;
				Quaternion invPrincipalAxesRot = Quaternion.Inverse(principalAxesRot);
				for (int j = 0; j < 3; j++)
				{
					Vector3 partInertiaTensorTimesjHat = principalAxesRot * Vector3.Scale(principalMoments, invPrincipalAxesRot * unitVectors[j]);
					for (int i = 0; i < 3; i++)
						InertiaTensor[i, j] += Vector3.Dot(unitVectors[i], partInertiaTensorTimesjHat);
				}
				//Compute the contributions to the vessel inertia tensor due to the part mass and position
				float partMass = p.TotalMass();
				Vector3 partPosition = vesselTransform.InverseTransformDirection(rb.worldCenterOfMass - wCoM);
				for(int i = 0; i < 3; i++)
				{
					InertiaTensor[i, i] += partMass * partPosition.sqrMagnitude;
					for (int j = 0; j < 3; j++)
						InertiaTensor[i, j] += -partMass * partPosition[i] * partPosition[j];
				}
			}
			MoI = new Vector3(InertiaTensor[0, 0], InertiaTensor[1, 1], InertiaTensor[2, 2]);
			MoI = refT.InverseTransformDirection(vessel.transform.TransformDirection(MoI));
		}
		#endregion
		#endregion
	}

	/// <summary>
	/// Binary flags of TCA state.
	/// They should to be checked in this particular order, as they are set sequentially:
	/// If a previous flag is not set, the next ones are not either.
	/// </summary>
	[Flags] public enum TCAState 
	{ 
		//basic state
		Disabled 			   = 0,
		Enabled 			   = 1 << 0,
		HaveEC 				   = 1 << 1, 
		HaveActiveEngines 	   = 1 << 2,
		Unoptimized			   = 1 << 3,
		//vertical flight
		VerticalSpeedControl   = 1 << 4,
		AltitudeControl        = 1 << 5,
		LoosingAltitude 	   = 1 << 6,
		//cruise radar
		ObstacleAhead	 	   = 1 << 7,
		GroundCollision	 	   = 1 << 8,
		Ascending		 	   = 1 << 9,
		//autopilot
		Scanning               = 1 << 10,
		Searching              = 1 << 11,
		CheckingSite           = 1 << 12,
		Landing                = 1 << 13,
		VTOLAssist             = 1 << 14,
		//composite
		Nominal				   = Enabled | HaveEC | HaveActiveEngines,
		NoActiveEngines        = Enabled | HaveEC,
		NoEC                   = Enabled,
	}
}

