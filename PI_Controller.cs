﻿/* Name: Throttle Controlled Avionics, Fork by Allis Tauri
 *
 * Authors: Quinten Feys & Willem van Vliet & Allis Tauri
 * License: BY: Attribution-ShareAlike 3.0 Unported (CC BY-SA 3.0): 
 * http://creativecommons.org/licenses/by-sa/3.0/
 * 
 */

using System;
using UnityEngine;

namespace ThrottleControlledAvionics
{
	public class PI_Controller : ConfigNodeObject
	{
		new public const string NODE_NAME = "PICONTROLLER";

		//buggy: need to be public to be persistent
		[Persistent] public float p = 0.5f, i = 0.5f; //some default values
		protected PI_Controller master;

		public float P { get { return master == null? p : master.P; } set { p = value; } }
		public float I { get { return master == null? i : master.I; } set { i = value; } }

		public void setPI(float P, float I) { p = P; i = I; }
		public void setPI(PI_Controller other) { p = other.P; i = other.I; }
		public void setMaster(PI_Controller master) { this.master = master; }

		public void DrawPIControls(string name)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label(name, GUILayout.ExpandWidth(false));
			p = Utils.FloatSlider(" P", P, 0, TCAConfiguration.Globals.MaxP, "F2");
			i = Utils.FloatSlider(" I", I, 0, TCAConfiguration.Globals.MaxI, "F2");
			GUILayout.EndHorizontal();
		}
	}

	public abstract class PI_Controller<T> : PI_Controller
	{
		protected T action = default(T);
		protected T integral_error = default(T);

		public abstract void Update(T error);

		public void Reset() 
		{ action = default(T); integral_error = default(T); }

		//access
		public T Action { get { return action; } }
		public static implicit operator T(PI_Controller<T> c) { return c.action; }
	}

	public class PI_Dummy : PI_Controller<int> 
	{ 
		public PI_Dummy() {}
		public PI_Dummy(float P, float I) { p = P; i = I; }
		public override void Update(int error) {}
	}

	public class PIv_Controller : PI_Controller<Vector3>
	{
		public override void Update(Vector3 error)
		{
			integral_error += error * TimeWarp.fixedDeltaTime;
			action = error * P + integral_error * I;
		}
	}

//	public class PIv_Controller2 : PI_Controller<Vector3>
//	{
//		[Persistent] float min = -1f, max = 1;
//
//		public PIv_Controller2(float p, float i, float min, float max)
//		{ this.p = p; this.i = i; this.min = min; this.max = max; }
//
//		public override void Update(Vector3 error)
//		{
//			integral_error += error * TimeWarp.fixedDeltaTime;
//
//			integral_error.x = (Math.Abs(derivativeAct.x) < 0.6 * max) ? integral_error.x + (error.x * Ki * TimeWarp.fixedDeltaTime) : 0.9 * integral_error.x;
//			integral_error.y = (Math.Abs(derivativeAct.y) < 0.6 * max) ? integral_error.y + (error.y * Ki * TimeWarp.fixedDeltaTime) : 0.9 * integral_error.y;
//			integral_error.z = (Math.Abs(derivativeAct.z) < 0.6 * max) ? integral_error.z + (error.z * Ki * TimeWarp.fixedDeltaTime) : 0.9 * integral_error.z;
//
//			action = error * P + integral_error * I;
//		}
//	}

	//I hate strongly-typed languages! =(
	public class PIf_Controller : PI_Controller<float>
	{
		public override void Update(float error)
		{
			integral_error += error * TimeWarp.fixedDeltaTime;
			action = error * P + integral_error * I;
		}
	}
}

