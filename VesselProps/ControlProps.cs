﻿//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2016 Allis Tauri
//
// This work is licensed under the Creative Commons Attribution-ShareAlike 4.0 International License. 
// To view a copy of this license, visit http://creativecommons.org/licenses/by-sa/4.0/ 
// or send a letter to Creative Commons, PO Box 1866, Mountain View, CA 94042, USA.
//
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace ThrottleControlledAvionics
{
	public class ControlProps : VesselProps
	{
		public ControlProps(VesselWrapper vsl) : base(vsl) {}

		public Transform Transform;
		public Vector3 Steering { get; private set; }
		public Vector3 Translation { get; private set; }
		public Vector3 AutopilotSteering;
		public bool    TranslationAvailable { get; private set; }
		public Vector3 ManualTranslation;
		public Switch  ManualTranslationSwitch = new Switch();
		public float   GimbalLimit = 100;
		public bool HaveControlAuthority = true;
		public double WarpToTime = -1;

		public void StopWarp() { WarpToTime = 0; }

		public override void Update()
		{
			Steering = new Vector3(vessel.ctrlState.pitch, vessel.ctrlState.roll, vessel.ctrlState.yaw);
			Translation = new Vector3(vessel.ctrlState.X, vessel.ctrlState.Z, vessel.ctrlState.Y);
			if(!Steering.IsZero()) //tune steering if MaxAA has changed drastically
				Steering = Steering*Utils.ClampH(VSL.Torque.MaxAAMod, 1)/Steering.CubeNorm().magnitude;
			if(!Translation.IsZero()) Translation = Translation/Translation.CubeNorm().magnitude;
			TranslationAvailable = VSL.Engines.Maneuver.Count > 0 || VSL.Engines.NumActiveRCS > 0;
		}

		public bool RCSAvailableInDirection(Vector3 wDir)
		{
			if(VSL.Engines.NumActiveRCS.Equals(0)) return false;
			var lDir = VSL.LocalDir(wDir).normalized;
			var thrust = VSL.Engines.MaxThrustRCS.Project(lDir);
//			Utils.LogF("\nMaxThrustRCS:\n{}\nRCS dir: {}\nRCS thrust: {}\nRCS accel: {}\nActive RCS: {}\n",
//			           VSL.Engines.MaxThrustRCS, lDir, thrust, thrust.magnitude/VSL.Physics.M, VSL.Engines.NumActiveRCS);//debug
			return thrust.magnitude/VSL.Physics.M > GLB.TRA.MinDeltaV/2;
		}

		void select_retT()
		{
			Part ref_part = null;
			var command_parts = VSL.vessel.parts.Where(p => p.HasModule<ModuleCommand>()).ToList();
			if(command_parts.Count == 0) ref_part = VSL.vessel.rootPart;
			else if(command_parts.Count == 1) ref_part = command_parts[0];
			else
			{
				
				float max_ali = -1;
				foreach(var p in command_parts)
				{
					var pos = (p.transform.position-VSL.Physics.wCoM).normalized;
					var ali = Vector3.Dot(p.transform.up, pos);
					if(ref_part == null ||  
					   (Math.Abs(max_ali-ali) < 1e-5 && ref_part.mass < p.mass) ||
					   max_ali < ali)
					{ ref_part = p; max_ali = ali; }
				}
			}
			CFG.ControlTransform = ref_part.flightID;
			Transform = ref_part.transform;
		}

		public void UpdateRefTransform()
		{
			if(CFG.ControlTransform == 0) select_retT();
			else
			{
				var ref_part = VSL.vessel.parts.Find(p => p.flightID == CFG.ControlTransform);
				if(ref_part == null) select_retT();
				else Transform = ref_part.transform;
			}
		}

		public void SetSteering(Vector3 steering)
		{ AutopilotSteering = steering.ClampComponents(-1, 1); }

		public void AddSteering(Vector3 steering)
		{ AutopilotSteering = (VSL.Controls.AutopilotSteering+steering).ClampComponents(-1, 1); }

		public void SetSteeringIfGrater(Vector3 steering)
		{
			if(Math.Abs(steering.x) > Math.Abs(AutopilotSteering.x)) 
				AutopilotSteering.x = Utils.Clamp(steering.x, -1, 1);
			if(Math.Abs(steering.y) > Math.Abs(AutopilotSteering.y)) 
				AutopilotSteering.y = Utils.Clamp(steering.y, -1, 1);
			if(Math.Abs(steering.z) > Math.Abs(AutopilotSteering.z)) 
				AutopilotSteering.z = Utils.Clamp(steering.z, -1, 1);
		}
	}
}

