using BDArmory.Control;
using BDArmory.Weapons;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        bool _yaw = false;

        int _blockedTurrets = 0; // Bitmask used for determining which turrets are blocked

        IEnumerator SortTurretListRoutine()
        {
            _sortingTurretList = true;

            yield return new WaitForFixedUpdate();

            turrets.Sort(delegate (ModuleTurret t1, ModuleTurret t2)
            {
                // We want them sorted from greatest to least
                int temp = t2.turretPriority.CompareTo(t1.turretPriority);

                if (temp != 0) 
                { 
                    return temp;
                }

                // If equal, sort from lowest turretID to highest
                return t1.turretID.CompareTo(t2.turretID);
            });

            for (int i = 0; i < turrets.Count; i++)
            {
                if (_yaw)
                {
                    turrets[i].yawAxisIndex = i;
                    if (i != 0)
                    {
                        turrets[i].DisableYawStandbyAngle();
                    }
                }
                else
                {
                    turrets[i].pitchAxisIndex = i;
                }
            }

            _sortingTurretList = false;
        }

        void SortTurretList()
        {
            if (!_sortingTurretList)
            {
                StartCoroutine(SortTurretListRoutine());
            }
        }

        // Checks for other turrets on this axis, returns true if successful
        public bool AddTurrets(Part part, bool yaw, Transform axisTransform)
        {
            // Need to null this in the calling turret
            if (axisTransform == null)
            {
                Destroy(this);
                return false;
            }

            _yaw = yaw;

            List<ModuleTurret> turr = part.FindModulesImplementing<ModuleTurret>();
            ModuleTurret currTurret;

            // Pre-allocate list
            turrets = new List<ModuleTurret>(turr.Count);
            _blockedTurrets = 0;

            for (int i = 0; i < turr.Count; i++)
            { 
                if ((currTurret = turr[i]) == null) continue;
                if (turrets.Contains(currTurret)) continue;

                currTurret.SetupTransforms();

                if ((_yaw ? currTurret.yawTransform : currTurret.pitchTransform) == axisTransform)
                {
                    turrets.Add(currTurret);

                    if (_yaw)
                    {
                        currTurret.yawAxisManager = this;
                    }
                    else
                    {
                        currTurret.pitchAxisManager = this;
                    }

                    // Default the turret to blocked, resolve it later when checking
                    _blockedTurrets |= 1 << i;
                }
            }

            // If there's no turrets somehow, or there's only one turret and there's no need to manage this axis, delete this
            if (turrets.Count <= 1)
            {
                Destroy(this);
                return false;
            }

            SortTurretList();

            return true;
        }

        public void SetYawStandbyAngle(ModuleTurret caller, float standbyAngle)
        {
            for (int i = 0; i < turrets.Count; i++)
            {
                if (turrets[i] == null) continue;
                if (turrets[i] == caller) continue;

                turrets[i].yawStandbyAngle = standbyAngle;
                turrets[i].SetStandbyAngle();
            }
        }

        // While the commanding weapon can get replaced when timeOfLastMoveCommand > Time.fixedTime
        // the turret can only move when timeOfLastMoveCommand < Time.fixedTime
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool CanMove()
        {
            if (timeOfLastMoveCommand > Time.fixedTime)
            {
                return false;
            }

            if (_blockedTurrets != 0)
            {
                DeployTurrets();
                return false;
            }

            timeOfLastMoveCommand = Time.fixedTime;

            return true;
        }

        void DeployTurrets()
        {
            float currBlockedTime = 0;
            for (int i = 0; i < turrets.Count; i++)
            {
                if ((_blockedTurrets & (1 << i)) != 0)
                {
                    float tempTime = turrets[i].DeployIfBlocking(_yaw);
                    if (tempTime > currBlockedTime) currBlockedTime = tempTime;
                }
            }

            if (currBlockedTime > timeOfLastMoveCommand) timeOfLastMoveCommand = currBlockedTime;
        }

        public bool CheckTurret(ModuleTurret t, bool returning, bool activeWeap = false)
        {
            // Special case, where we've previously confirmed all turrets were disabled
            if (currTurretIndex < 0)
            {
                // If not returning, set currTurretIndex
                // NOTE: returning does NOT include missile reload Return()!
                if (!returning)
                {
                    currTurretIndex = GetTurretIndex(t);
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
                    if (turrets[i] == null) continue;
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

            // If the current turret is the turret trying to move, and it's not returning, allow it to do so
            if (currTurret && currTurret == t)
            {
                return CanMove();
            }

            // If we're forcing the movement, E.G. we're the current weapon or we're in slavedGuard
            if (activeWeap)
            {
                // If there's a turret, check if it has higher priority, and if it is also part of the
                // current weapon set, if so, then prioritize that
                if (currTurret && currTurret.turretPriority >= t.turretPriority &&
                    ((currTurret.turretWeapon && currTurret.turretWeapon.IsCurrentWMWeapon()) ||
                    (currTurret.turretMissile && currTurret.turretMissile.IsCurrentWMMissile())))
                {
                    return false;
                }

                currTurretIndex = GetTurretIndex(t);
                return CanMove();
            }

            int startIndex;

            // If currTurret exists...
            if (currTurret)
            {
                // If the current turret's priority is > t's
                if (currTurret.turretPriority >= t.turretPriority)
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
                    // the list starting from the highest priority turret. While this is technically
                    // less efficient in the case where t is the highest priority turret active, and
                    // there are higher priority turrets than t that are not active, this does prevent
                    // repeated replacements of currTurretIndex and incorrectly prioritized turret
                    // movements
                    startIndex = 0;
                }

                // Note that we do the above checks first, instead of after these override checks,
                // as the priority -> enabled return path is faster than checking what's below

                // No overriding current weapon / missile or slavedGuard
                if (IsCurrentWMTurr(currTurret))
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
            currTurretIndex = GetTurretIndex(t);
            return CanMove();
        }

        void OnDestroy()
        {
            if (turrets == null) return;

            for (int i = 0; i < turrets.Count; i++)
            {
                if (turrets[i] == null) continue;

                if (_yaw)
                {
                    turrets[i].yawAxisManager = null;
                }
                else
                {
                    turrets[i].pitchAxisManager = null;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetTurretIndex(ModuleTurret t)
        {
            return _yaw ? t.yawAxisIndex : t.pitchAxisIndex;
        }

        // Block axis from moving, E.G. for missile turret reloading
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTurretBlock(float time)
        {
            if (timeOfLastMoveCommand < time)
            {
                timeOfLastMoveCommand = time;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCurrentWMTurr(ModuleTurret t)
        {
            return (t.turretWeapon && t.turretWeapon.IsCurrentWMWeapon()) || (t.turretMissile && t.turretMissile.IsCurrentWMMissile());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTurretFlag(bool blocked, int index)
        {
            if (blocked)
            {
                _blockedTurrets |= 1 << index;
            }
            else
            {
                _blockedTurrets &= ~(1 << index);
            }
        }
    }
}
