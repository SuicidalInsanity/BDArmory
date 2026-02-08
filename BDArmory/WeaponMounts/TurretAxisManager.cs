using BDArmory.Control;
using BDArmory.Weapons;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.WeaponMounts
{
    public class TurretAxisManager : MonoBehaviour
    {
        // Set when any command issued (Aim / Return), no command may be issued until Time.time > timeOfLastMoveCommand
        // this is used both for regular movements, to prevent two turrets from making commands in the same frame as
        // well as to lock the turret during some action, E.G. deployment of something, or missile turret reloading etc.
        public float timeOfLastMoveCommand = 0;
        
        // Current index of the turret in command
        public int currTurretIndex = -1;

        // Sorted list of turrets, based on priority
        public List<ModuleTurret> turrets;

        bool _sortingTurretList = false;

        IEnumerator SortTurretListRoutine()
        {
            _sortingTurretList = true;

            yield return new WaitForFixedUpdate();

            turrets.Sort(delegate (ModuleTurret t1, ModuleTurret t2)
            {
                // We want them sorted from greatest to least
                return t2.turretPriority.CompareTo(t1.turretPriority);
            });

            _sortingTurretList = false;
        }

        void SortTurretList()
        {
            if (!_sortingTurretList)
            {
                StartCoroutine(SortTurretListRoutine());
            }
        }

        public void AddTurrets(Part part, bool yaw, Transform axisTransform)
        {
            using (List<ModuleTurret>.Enumerator turr = part.FindModulesImplementing<ModuleTurret>().GetEnumerator())
                while (turr.MoveNext())
                {
                    if (turr.Current == null) continue;
                    if (turrets.Contains(turr.Current)) continue;

                    turr.Current.SetupTransforms();

                    if ((yaw ? turr.Current.yawTransform : turr.Current.pitchTransform) == axisTransform)
                    {
                        turrets.Add(turr.Current);

                        if (yaw)
                        {
                            turr.Current.yawAxisManager = this;
                        }
                        else
                        {
                            turr.Current.pitchAxisManager = this;
                        }
                    }
                }

            // If there's no turrets somehow, or there's only one turret and there's no need to manage it, delete this
            if (turrets.Count <= 1)
            {
                Destroy(this);
            }

            SortTurretList();
        }

        // While the commanding weapon can get replaced when timeOfLastMoveCommand > Time.fixedTime
        // the turret can only move when timeOfLastMoveCommand < Time.fixedTime
        bool CanMove()
        {
            if (timeOfLastMoveCommand > Time.fixedTime)
            {
                return false;
            }

            timeOfLastMoveCommand = Time.fixedTime;

            return true;
        }

        public bool CheckTurret(ModuleTurret t, bool returning, bool forced = false)
        {
            // Special case, where we've previously confirmed all turrets were disabled
            if (currTurretIndex < 0)
            {
                // If not returning, set currTurretIndex
                // NOTE: returning does NOT include missile reload Return()!
                if (!returning)
                {
                    currTurretIndex = turrets.IndexOf(t);
                }

                // And return CanMove()
                return CanMove();
            }

            ModuleTurret currTurret = turrets[currTurretIndex];

            // If we want to return the turret...
            if (returning)
            {
                // If the current turret is enabled return false
                if (currTurret && currTurret.turretEnabled())
                {
                    return false;
                }

                // Check all turrets to see if any are enabled...
                for (int i = 0; i < turrets.Count; i++)
                {
                    if (turrets[i].turretEnabled())
                    {
                        currTurretIndex = i;
                        return false;
                    }
                }

                // If none are, set currTurretIndex to -1 and return CanMove()
                currTurretIndex = -1;
                return CanMove();
            }

            // If we're forcing the movement, E.G. weapon is current weapon, or we're slavedGuard
            if (forced)
            {
                currTurretIndex = turrets.IndexOf(t);
                return CanMove();
            }

            // If the current turret is the turret trying to move, and it's not returning, allow it to do so
            if (currTurret == t)
            {
                return CanMove();
            }

            int startIndex;

            // If currTurret exists...
            if (currTurret)
            {
                // If the current turret's priority is > t's
                if (currTurret.turretPriority > t.turretPriority)
                {
                    // If it's enabled, return false
                    if (currTurret.turretEnabled())
                    {
                        return false;
                    }

                    // Otherwise, we have to check if there's any higher priority turrets enabled,
                    // starting from currTurretIndex as currTurret comes before t in the list
                    startIndex = currTurretIndex;
                }
                else
                {
                    // If the priority of the current turret is lower than t's, then we must check
                    // the list starting from the highest priority turret, which doubles as a
                    // IndexOf operation
                    startIndex = 0;
                }

                // Note that we do the above checks first, instead of after these override checks,
                // as the priority -> enabled return path is faster than checking what's below

                // No overriding current weapon / missile or slavedGuard
                if ((currTurret.turretWeapon && currTurret.turretWeapon.IsCurrentWMWeapon()) ||
                    (currTurret.turretMissile && currTurret.turretMissile.IsCurrentWMMissile()))
                {
                    return false;
                }
            }
            else
            {
                startIndex = 0;
            }

            for (int i = startIndex; i < turrets.Count; i++)
            {
                if (turrets[i] == null) continue;

                // If the highest priority turret currently enabled is
                // our own turret, set currTurretIndex to it, and return CanMove()
                // This is essentially like calling IndexOf
                if (turrets[i] == t)
                {
                    currTurretIndex = i;
                    return CanMove();
                }

                // Otherwise, if there's a higher priority turret currently
                // enabled, set currTurretIndex to it, and return false
                if (turrets[i].turretEnabled())
                {
                    currTurretIndex = i;
                    return false;
                }
            }

            // This *shouldn't* happen but if somehow everything breaks,
            // set currTurretIndex to t's index and return CanMove()
            currTurretIndex = turrets.IndexOf(t);
            return CanMove();
        }

        // Block axis from moving, E.G. for missile turret reloading
        public void SetTurretBlock(float duration)
        {
            timeOfLastMoveCommand = Time.fixedTime + duration;
        }
    }
}
