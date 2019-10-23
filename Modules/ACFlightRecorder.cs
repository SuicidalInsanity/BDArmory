using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BDArmory.Modules
{
	public class ACFlightRecorder : PartModule
	{
		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Trail Width"), UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
		public float TrailWidth = 1f;

		private float updateTimer = 0;
		Color TrailColor = XKCDColors.Grey;
		MissileFire mf;

		[KSPField(isPersistant = true)]
		public bool TrailEnabled = true;

		[KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Disable Trail", active = true)]
		public void ToggleEngageOptions()
		{
			TrailEnabled = !TrailEnabled;

			if (TrailEnabled == false)
			{
				Events["ToggleEngageOptions"].guiName = "Enable Trail";
			}
			else
			{
				Events["ToggleEngageOptions"].guiName = "Disable Trail";
			}

			Fields["Trail Width"].guiActive = TrailEnabled;
			Fields["Trail Width"].guiActiveEditor = TrailEnabled;

			Misc.Misc.RefreshAssociatedWindows(part);


		}
		public MissileFire weaponManager
		{
			get
			{
				if (mf) return mf;
				List<MissileFire>.Enumerator wm = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
				while (wm.MoveNext())
				{
					if (wm.Current == null) continue;
					mf = wm.Current;
					break;
				}
				wm.Dispose();
				return mf;
			}
		}
		void GetTeamID()
		{
			if (mf.Team.Name == "A")
			{
				if (vessel.isActiveVessel && vessel.IsControllable)
				{
					TrailColor = XKCDColors.Red;
				}
				else
					TrailColor = XKCDColors.Rose;
			}
			else if (mf.Team.Name == "B")
			{
				if (vessel.isActiveVessel && vessel.IsControllable)
				{
					TrailColor = XKCDColors.Blue;
				}
				else
					TrailColor = XKCDColors.LightBlue;
			}

		}
		private void Update()
		{
			if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && vessel.IsControllable)
			{
				List<Vector3> pointPositions = new List<Vector3>();
				updateTimer -= Time.fixedDeltaTime;
				if (updateTimer < 0)
				{
					updateTimer = 1f;    //next update once a sec
					GetTeamID();
					pointPositions.Add(vessel.CoM);
				}

				if (TrailEnabled)
				{
					Vector3[] pointsArray = pointPositions.ToArray();
					if (gameObject.GetComponent<LineRenderer>() == null)
					{
						LineRenderer VesselTrail = gameObject.AddComponent<LineRenderer>();
						VesselTrail.startWidth = TrailWidth;
						VesselTrail.endWidth = TrailWidth;
						VesselTrail.startColor = TrailColor;
						VesselTrail.positionCount = pointsArray.Length;
						for (int i = 0; i < pointsArray.Length; i++)
						{
							VesselTrail.SetPosition(i, pointsArray[i]);
						}
					}
					else
					{
						LineRenderer VesselTrail = gameObject.GetComponent<LineRenderer>();
						VesselTrail.enabled = true;
						VesselTrail.positionCount = pointsArray.Length;
						for (int i = 0; i < pointsArray.Length; i++)
						{
							VesselTrail.SetPosition(i, pointsArray[i]);
						}
					}
				}
				if (!TrailEnabled)
				{
					LineRenderer VesselTrail = gameObject.GetComponent<LineRenderer>();
					VesselTrail.enabled = false;
					pointPositions.Clear();
				}
			}
		}

	}
}
