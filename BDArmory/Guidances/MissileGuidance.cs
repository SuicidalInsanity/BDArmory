﻿using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.Weapons;

namespace BDArmory.Guidances
{
    public class MissileGuidance
    {
        public static Vector3 GetAirToGroundTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float descentRatio, float minSpeed = 200)
        {
            // Incorporate lead for target velocity
            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);
            float leadTime = Mathf.Clamp(targetDistance / (targetVelocity - currVel).magnitude, 0f, 8f);
            targetPosition += targetVelocity * leadTime;

            Vector3 upDirection = missileVessel.up;
            Vector3 surfacePos = missileVessel.CoM +
                                 Vector3.Project(targetPosition - missileVessel.CoM, upDirection);
            //((float)missileVessel.altitude*upDirection);
            Vector3 targetSurfacePos;

            targetSurfacePos = targetPosition;

            float distanceToTarget = Vector3.Distance(surfacePos, targetSurfacePos);

            if (missileVessel.srfSpeed < 75 && missileVessel.verticalSpeed < 10)
            //gain altitude if launching from stationary
            {
                return missileVessel.CoM + (5 * missileVessel.transform.forward) + (1 * upDirection);
            }

            float altitudeClamp = Mathf.Clamp(
                (distanceToTarget - ((float)missileVessel.srfSpeed * descentRatio)) * 0.22f, 0,
                (float)missileVessel.altitude);

            //Debug.Log("[BDArmory.MissileGuidance]: AGM altitudeClamp =" + altitudeClamp);
            Vector3 finalTarget = targetPosition + (altitudeClamp * upDirection.normalized);

            //Debug.Log("[BDArmory.MissileGuidance]: Using agm trajectory. " + Time.time);

            return finalTarget;
        }

        public static bool GetBallisticGuidanceTarget(Vector3 targetPosition, Vessel missileVessel, bool direct,
            out Vector3 finalTarget)
        {
            Vector3 up = missileVessel.up;
            Vector3 forward = (targetPosition - missileVessel.CoM).ProjectOnPlanePreNormalized(up);
            float speed = (float)missileVessel.srfSpeed;
            float sqrSpeed = speed * speed;
            float sqrSpeedSqr = sqrSpeed * sqrSpeed;
            float g = (float)FlightGlobals.getGeeForceAtPosition(missileVessel.CoM).magnitude;
            float height = FlightGlobals.getAltitudeAtPos(targetPosition) -
                           FlightGlobals.getAltitudeAtPos(missileVessel.CoM);
            float sqrRange = forward.sqrMagnitude;
            float range = BDAMath.Sqrt(sqrRange);

            float plusOrMinus = direct ? -1 : 1;

            float top = sqrSpeed + (plusOrMinus * BDAMath.Sqrt(sqrSpeedSqr - (g * ((g * sqrRange + (2 * height * sqrSpeed))))));
            float bottom = g * range;
            float theta = Mathf.Atan(top / bottom);

            if (!float.IsNaN(theta))
            {
                Vector3 finalVector = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.Cross(forward, up)) * forward;
                finalTarget = missileVessel.CoM + (100 * finalVector);
                return true;
            }
            else
            {
                finalTarget = Vector3.zero;
                return false;
            }
        }

        public static bool GetBallisticGuidanceTarget(Vector3 targetPosition, Vector3 missilePosition,
            float missileSpeed, bool direct, out Vector3 finalTarget)
        {
            Vector3 up = VectorUtils.GetUpDirection(missilePosition);
            Vector3 forward = (targetPosition - missilePosition).ProjectOnPlanePreNormalized(up);
            float speed = missileSpeed;
            float sqrSpeed = speed * speed;
            float sqrSpeedSqr = sqrSpeed * sqrSpeed;
            float g = (float)FlightGlobals.getGeeForceAtPosition(missilePosition).magnitude;
            float height = FlightGlobals.getAltitudeAtPos(targetPosition) -
                           FlightGlobals.getAltitudeAtPos(missilePosition);
            float sqrRange = forward.sqrMagnitude;
            float range = BDAMath.Sqrt(sqrRange);

            float plusOrMinus = direct ? -1 : 1;

            float top = sqrSpeed + (plusOrMinus * BDAMath.Sqrt(sqrSpeedSqr - (g * ((g * sqrRange + (2 * height * sqrSpeed))))));
            float bottom = g * range;
            float theta = Mathf.Atan(top / bottom);

            if (!float.IsNaN(theta))
            {
                Vector3 finalVector = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.Cross(forward, up)) * forward;
                finalTarget = missilePosition + (100 * finalVector);
                return true;
            }
            else
            {
                finalTarget = Vector3.zero;
                return false;
            }
        }

        public static Vector3 GetBeamRideTarget(Ray beam, Vector3 currentPosition, Vector3 currentVelocity,
            float correctionFactor, float correctionDamping, Ray previousBeam)
        {
            float onBeamDistance = Vector3.Dot(currentPosition - beam.origin, beam.direction); //Vector3.Project(currentPosition - beam.origin, beam.direction).magnitude;
            //Vector3 onBeamPos = beam.origin+Vector3.Project(currentPosition-beam.origin, beam.direction);//beam.GetPoint(Vector3.Distance(Vector3.Project(currentPosition-beam.origin, beam.direction), Vector3.zero));
            Vector3 onBeamPos = beam.GetPoint(onBeamDistance);
            Vector3 previousBeamPos = previousBeam.GetPoint(onBeamDistance);
            Vector3 beamVel = (onBeamPos - previousBeamPos) / Time.fixedDeltaTime;
            Vector3 target = onBeamPos + (500f * beam.direction);
            Vector3 offset = onBeamPos - currentPosition;
            offset += beamVel * 0.5f;
            target += correctionFactor * offset;

            Vector3 velDamp = correctionDamping * (currentVelocity - beamVel).ProjectOnPlanePreNormalized(beam.direction);
            target -= velDamp;

            return target;
        }

        public static Vector3 GetAirToAirTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, out float timeToImpact, float minSpeed = 200)
        {
            float leadTime = 0;
            float RSqr = Vector3.SqrMagnitude(targetPosition - missileVessel.CoM);

            Vector3 currVel = Mathf.Max((float)missileVessel.srfSpeed, minSpeed) * missileVessel.Velocity().normalized;

            Vector3 Rdir = (targetPosition - missileVessel.CoM);
            leadTime = -RSqr / Vector3.Dot(targetVelocity - currVel, Rdir);

            if (leadTime <= 0f)
                leadTime = float.PositiveInfinity;

            //leadTime = targetDistance / (targetVelocity - currVel).magnitude;
            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);

            return targetPosition + (targetVelocity * leadTime);
        }

        public static Vector3 GetWeaveTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, ref float gVert, ref float gHorz, float gRand, float omega, float terminalAngle, float weaveFactor, bool useAGMDescentRatio, float agmDescentRatio, ref float weaveOffset, ref Vector3 weaveStart, out float ttgo, out float gLimit)
        {
            // Based on https://www.sciencedirect.com/science/article/pii/S1474667015333437

            Vector3 missileVel = missileVessel.Velocity();
            float speed = (float)missileVessel.speed;

            // Time to go calculation according to instantaneous change in range (dR/dt)
            Vector3 Rdir = (targetPosition - missileVessel.CoM);
            ttgo = -Rdir.sqrMagnitude / Vector3.Dot(targetVelocity - missileVel, Rdir);

            if (BDArmorySettings.DEBUG_MISSILES)
                Debug.Log($"[BDArmory.MissileGuidance] targetPosition: {targetPosition}, targetVelocity: {targetVelocity}, missileVel: {missileVel}, missileSpeed: {speed}, Rdir.sqrMag: {Rdir.sqrMagnitude}, ttgo: {ttgo}");

            if (ttgo <= 0f)
            {
                // Missed target, use PN as backup
                return GetPNTarget(targetPosition, targetVelocity, missileVessel, 3, out ttgo, out gLimit);
            }

            // Get up direction at missile location
            Vector3 upDirection = missileVessel.upAxis;

            // High pass filter
            if (targetVelocity.sqrMagnitude > 100f)
                Rdir = new Vector3(Rdir.x + targetVelocity.x * ttgo,
                                    Rdir.y + targetVelocity.y * ttgo,
                                    Rdir.z + targetVelocity.z * ttgo);

            Vector3 planarDirToTarget = Rdir.ProjectOnPlanePreNormalized(upDirection).normalized;

            Vector3 right = Vector3.Cross(planarDirToTarget, upDirection);

            float verticalAngle = (Mathf.Deg2Rad * Mathf.Sign(Vector3.Dot(missileVel, upDirection))) * Vector3.Angle(missileVel.ProjectOnPlanePreNormalized(right), planarDirToTarget);

            float horizontalAngle = (Mathf.Deg2Rad * Mathf.Sign(Vector3.Dot(missileVel, right))) * Vector3.Angle(missileVel.ProjectOnPlanePreNormalized(upDirection), planarDirToTarget);

            const float PI2 = 2f * Mathf.PI;

            float weaveDist;

            float ttgoWeave;
            if (weaveOffset < 0)
            {
                weaveOffset = PI2 * omega * ttgo;
                weaveStart = VectorUtils.WorldPositionToGeoCoords(missileVessel.CoM, missileVessel.mainBody);
                weaveDist = Vector3.Dot(Rdir, planarDirToTarget);
                ttgoWeave = ttgo;
                if (UnityEngine.Random.value < 0.5)
                    gHorz = -gHorz;

                if (gVert != 0.0f)
                    gVert += gRand * (2f * UnityEngine.Random.value - 1f);
                if (gHorz != 0.0f)
                    gHorz += gRand * (2f * UnityEngine.Random.value - 1f);
            }
            else
            {
                Vector3 weaveDir = (targetPosition - VectorUtils.GetWorldSurfacePostion(weaveStart, missileVessel.mainBody)).ProjectOnPlanePreNormalized(upDirection).normalized;
                weaveDist = Vector3.Dot(Rdir, weaveDir);
                ttgoWeave = weaveFactor * 1.5f * weaveDist / speed;
                right = Vector3.Cross(weaveDir, upDirection);
            }
                    
            float omegaBeta = PI2 * omega * ttgoWeave;
            float sinOmegaBeta = Mathf.Sin(omegaBeta);
            float cosOmegaBeta = Mathf.Cos(omegaBeta);

            float sinOmegaBetaOff = Mathf.Sin(omegaBeta - weaveOffset);
            float cosOmegaBetaOff = Mathf.Cos(omegaBeta - weaveOffset);

            const float g = 9.80665f;
            float ka = 2f * omegaBeta * sinOmegaBeta + 6f * cosOmegaBeta - 6f;
            float kj = -2f * omegaBeta * cosOmegaBeta + 6f * sinOmegaBeta - 4f * omegaBeta;

            float ttgoWeaveInv = 1f / ttgoWeave;
            float omegaBetaInv = 1f / (Mathf.Max(omegaBeta * omegaBeta, 0.000001f));

            float aVert = ((terminalAngle > 0) ? (speed * (6f * Mathf.Asin(Vector3.Dot(upDirection, Rdir) / Rdir.magnitude) - 4f * verticalAngle + 2f * terminalAngle * Mathf.Deg2Rad) * ttgoWeaveInv) : 0.0f) // A_BPN                                                                                                                                    
                + ((gVert != 0.0f) ? (gVert * g * ((ka + omegaBeta * omegaBeta) * sinOmegaBetaOff + kj * cosOmegaBetaOff) * omegaBetaInv) : 0.0f); // A_W
            float aHor = ((terminalAngle > 0) ? (-6f * speed * horizontalAngle) * ttgoWeaveInv : 0.0f) // A_BPN            
                + ((gHorz != 0.0f) ? (gHorz * g * ((ka + omegaBeta * omegaBeta) * cosOmegaBetaOff + kj * sinOmegaBetaOff) * omegaBetaInv) : 0.0f); // A_W

            Quaternion rotationPitch = Quaternion.AngleAxis(verticalAngle, right);
            Quaternion rotationYaw = Quaternion.AngleAxis(horizontalAngle, upDirection);

            Vector3 accel = (aVert * (rotationPitch * rotationYaw * upDirection) + aHor * (rotationYaw * right));// + GetPNAccel(targetPosition, targetVelocity, missileVessel, 3f);
            if (terminalAngle < 0)
            {
                accel += GetPNAccel(targetPosition, targetVelocity, missileVessel, 3f);
                gLimit = accel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            }
            else
                gLimit = BDAMath.Sqrt(aVert * aVert + aHor * aHor) / (float)PhysicsGlobals.GravitationalAcceleration;

            if (BDArmorySettings.DEBUG_MISSILES)
                Debug.Log($"[BDArmory.MissileGuidance] Weave guidance ttgoWeave: {ttgoWeave}, omegaBeta: {omegaBeta}, ka: {ka}, kj: {kj}, vertAngle: {Mathf.Rad2Deg * verticalAngle}, horAngle: {Mathf.Rad2Deg * horizontalAngle}, aVert: {aVert} m/s^2, aHor: {aHor} m/s^2.");

            float leadTime = Mathf.Min(4f, ttgoWeave);

            Vector3 aimPos = missileVessel.CoM + leadTime * missileVel + accel * (0.5f * leadTime * leadTime);

            if (useAGMDescentRatio)
            {
                float altitudeClamp = Mathf.Clamp(
                    (weaveDist - ((float)missileVessel.srfSpeed * agmDescentRatio)) * 0.22f, 0f,
                    (float)missileVessel.altitude +
                    Mathf.Max(VectorUtils.AnglePreNormalized(upDirection, missileVel.normalized) - 90f, 0f) * Mathf.Deg2Rad * weaveDist);

                float altDiff = Vector3.Dot(aimPos - missileVessel.CoM, upDirection) + (float)missileVessel.altitude - Mathf.Max(FlightGlobals.getAltitudeAtPos(targetPosition), -2f);

                if (altDiff < altitudeClamp)
                {
                    aimPos += (altitudeClamp - altDiff) * upDirection;
                }
            }

            return aimPos;
        }

        // Kappa/Trajectory Curvature Optimal Guidance 
        public static Vector3 GetKappaTarget(Vector3 targetPosition, Vector3 targetVelocity,
            MissileLauncher ml, float thrust, float shapingAngle, float rangeFac, float vertVelComp,
            float targetAlt, float terminalHomingRange, float loftAngle, float loftTermAngle, 
            float midcourseRange, float maxAltitude, out float ttgo, out float gLimit,
            ref MissileBase.LoftStates loftState)
        {
            // Get surface velocity direction
            Vector3 velDirection = ml.vessel.srf_vel_direction;

            // Get range
            float R = Vector3.Distance(targetPosition, ml.vessel.CoM);
            // Unfortunately can't be simplified as R is needed later on

            // Kappa Guidance needs an accurate measure of speed to function so no minSpeed application here
            float currSpeed = (float)ml.vessel.srfSpeed;
            // Set current velocity
            Vector3 currVel = currSpeed * velDirection;

            // Old Method
            //float leadTime = R / (targetVelocity - currVel).magnitude;
            //leadTime = Mathf.Clamp(leadTime, 0f, 16f);

            // Time to go calculation according to instantaneous change in range (dR/dt)
            Vector3 Rdir = (targetPosition - ml.vessel.CoM);
            ttgo = -R*R / Vector3.Dot(targetVelocity - currVel, Rdir);

            if (ttgo <= 0f)
                ttgo = 60f;

            float ttgoInv = 1f / ttgo;

            float leadTime = Mathf.Clamp(ttgo, 0f, 16f);

            // Get up direction at missile location
            Vector3 upDirection = ml.vessel.upAxis; //VectorUtils.GetUpDirection(ml.vessel.CoM);

            // Set up PIP vector
            Vector3 predictedImpactPoint = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime + TimeWarp.fixedDeltaTime);

            float sinTarget = Vector3.Dot((predictedImpactPoint - ml.vessel.CoM).normalized, -upDirection); //Vector3.Dot((targetPosition - ml.vessel.CoM), -upDirection) / R;

            // If still in boost phase
            if ((loftState < MissileBase.LoftStates.Midcourse) && (midcourseRange > 0f) && (R > midcourseRange) && (sinTarget < Mathf.Sin(loftTermAngle * Mathf.Deg2Rad)) && (-sinTarget < Mathf.Sin(loftAngle * Mathf.Deg2Rad)))
            {
                Vector3 planarDirectionToTarget = ((predictedImpactPoint - ml.vessel.CoM).ProjectOnPlanePreNormalized(upDirection)).normalized;

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Lofting");

                float altitudeClamp = Mathf.Clamp(targetAlt + 10f * rangeFac * Mathf.Pow(Vector3.Dot(targetPosition - ml.vessel.CoM, planarDirectionToTarget), Mathf.Abs(vertVelComp)), targetAlt, Mathf.Max(maxAltitude, targetAlt));

                // Stolen from my AAMloft guidance
                // Limit climb angle by turnFactor, turnFactor goes negative when above target alt
                float turnFactor = (float)(altitudeClamp - ml.vessel.altitude) / (4f * currSpeed);
                turnFactor = Mathf.Clamp(turnFactor, -1f, 1f);

                // Limit gs during climb
                gLimit = 6f;

                return ml.vessel.CoM + currSpeed * ((Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad) * upDirection));
            }
            else
            {
                // Accurately predict impact point
                //predictedImpactPoint = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, ttgo + TimeWarp.fixedDeltaTime);

                // Final velocity is shaped by shapingAngle, we want the missile to dive onto the target but we don't want to affect the
                // horizontal components of velocity
                Vector3 vF;
                if (shapingAngle == 0f)
                {
                    vF = currVel;
                }
                else
                {
                    vF = velDirection.ProjectOnPlanePreNormalized(upDirection).normalized;
                    vF = currSpeed * (Mathf.Cos(shapingAngle * Mathf.Deg2Rad) * vF - Mathf.Sin(shapingAngle * Mathf.Deg2Rad) * upDirection);
                }

                // Gains for velocity error and positional error
                float K1;
                float K2;

                // If we're above terminal homing range
                if ((loftState < MissileBase.LoftStates.Terminal) && (R > terminalHomingRange) && !ml.vessel.InVacuum())
                {
                    loftState = MissileBase.LoftStates.Midcourse;

                    if (shapingAngle != 0f)
                    {
                        // As we get closer to the target we want to focus on the positional error, not the velocity error
                        float factor = Mathf.Min(0.5f * (R - terminalHomingRange) / terminalHomingRange, 1f);
                        vF = factor * vF + (1f - factor) * currVel;
                    }

                    // Dynamic pressure times the lift area
                    float q = (float)(0.5f * ml.vessel.atmDensity * ml.vessel.srfSpeed * ml.vessel.srfSpeed);

                    // Needs to be changed if the lift and drag curves are changed
                    float Lalpha = 2.864788975654117f * q * ml.liftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER; // CLmax/AoA(CLmax) * q * S * Lift Multiplier, I.E. linearized Lift/AoA (not CL/AoA)
                    float D0 = 0.00215f * q * ml.dragArea * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER; // Drag at 0 AoA
                    float eta = 0.025f * BDArmorySettings.GLOBAL_DRAG_MULTIPLIER * ml.dragArea / (BDArmorySettings.GLOBAL_LIFT_MULTIPLIER * ml.liftArea); // D = D0 + eta*Lalpha*AoA^2, quadratic approximation of drag.
                    // eta needs to change if the lift/drag curves are changed. Note this is for small angles

                    // Pre-calculation since it's used a lot
                    float TL = thrust / Lalpha;

                    // Ching-Fang Lin's derivation of a missile under thrust. Doesn't work well best I can tell.
                    //if (thrust > D0)
                    //{
                    //    float F2sqr = Lalpha * (thrust - D0) * (TL * TL + 1f) * (TL * TL + 1f) / ((float)(ml.vessel.totalMass * ml.vessel.totalMass * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * (2 * eta + TL));
                    //    float F2 = BDAMath.Sqrt(Mathf.Abs(F2sqr));

                    //    float sinF2R = Mathf.Sin(F2 * R);
                    //    float cosF2R = Mathf.Cos(F2 * R);

                    //    K1 = F2 * R * (sinF2R - F2 * R) / (2f - 2f * cosF2R - F2 * R * sinF2R);
                    //    K2 = F2sqr * R * R * (1f - cosF2R) / (2f - 2f * cosF2R - F2 * R * sinF2R);
                    //}
                    //else
                    //{
                        // General derivation of aerodynamic constant for Kappa guidance
                        float Fsqr = D0 * Lalpha * (TL + 1) * (TL + 1) / ((float)(ml.vessel.totalMass * ml.vessel.totalMass * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * (2 * eta + TL));
                        float F = BDAMath.Sqrt(Fsqr);

                        float eFR = Mathf.Exp(F * R);
                        float enFR = Mathf.Exp(-F * R);

                        K1 = (2f * Fsqr * R * R - F * R * (eFR - enFR)) / (eFR * (F * R - 2f) - enFR * (F * R + 2f) + 4f);
                        K2 = (Fsqr * R * R * (eFR + enFR - 2f)) / (eFR * (F * R - 2f) - enFR * (F * R + 2f) + 4f);
                    //}
                }
                else
                {
                    loftState = MissileBase.LoftStates.Terminal;
                    // Optimal gains if we ignore aerodynamic effects. In the terminal phase we can neglect these
                    K1 = -2f;
                    K2 = 6f;

                    // Technically equivalent to setting K1 = 0
                    vF = currVel;
                }

                // Acceleration per Kappa guidance
                Vector3 accel = (K1 * ttgoInv) * (vF - currVel) + (K2 * ttgoInv * ttgoInv) * (predictedImpactPoint - ml.vessel.CoM - currVel * ttgo);
                accel = accel.ProjectOnPlanePreNormalized(velDirection);
                // gLimit is based solely on acceleration normal to the velocity vector, technically this guidance law gives
                // both normal acceleration and tangential acceleration but we can only really manage normal acceleration
                gLimit = (accel).magnitude / (float)PhysicsGlobals.GravitationalAcceleration;

                // Debug output, useful for tuning
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Kappa Guidance K1: {K1}, K2: {K2}, accel: {accel}, vF-currVel: {vF-currVel}, posError: {predictedImpactPoint- ml.vessel.CoM - currVel*ttgo}, g: {gLimit}, ttgo: {ttgo}");

                return ml.vessel.CoM + currVel * Mathf.Min(leadTime, 3f) + accel * Mathf.Min(leadTime * leadTime, 9f);
            }
        }

        public static Vector3 GetAirToAirLoftTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float targetAlt, float maxAltitude,
            float rangeFactor, float vertVelComp, float velComp, float loftAngle, float termAngle,
            float termDist, ref MissileBase.LoftStates loftState, out float timeToImpact, out float gLimit,
            out float targetDistance, MissileBase.GuidanceModes homingModeTerminal, float N,
            float minSpeed = 200)
        {
            Vector3 velDirection = missileVessel.srf_vel_direction; //missileVessel.Velocity().normalized;

            targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);

            float currSpeed;// = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);

            if (loftState < MissileBase.LoftStates.Midcourse)
                currSpeed = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);
            else
                currSpeed = (float)missileVessel.srfSpeed;

            Vector3 currVel = currSpeed * velDirection;

            //Vector3 Rdir = (targetPosition - missileVessel.transform.position).normalized;
            //float rDot = Vector3.Dot(targetVelocity - currVel, Rdir);

            float leadTime = targetDistance / (targetVelocity - currVel).magnitude;
            //float leadTime = (targetDistance / rDot);

            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 16f);

            gLimit = -1f;

            // If loft is not terminal
            if ((loftState < MissileBase.LoftStates.Terminal) && (targetDistance > termDist))
            {
                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Lofting");

                // Get up direction
                Vector3 upDirection = missileVessel.upAxis; //VectorUtils.GetUpDirection(missileVessel.CoM);

                // Use the gun aim-assist logic to determine ballistic angle (assuming no drag)
                Vector3 missileRelativePosition, missileRelativeVelocity, missileAcceleration, missileRelativeAcceleration, targetPredictedPosition, missileDropOffset, lastVelDirection, ballisticTarget, targetHorVel, targetCompVel;

                var firePosition = missileVessel.CoM; //+ (currSpeed * velDirection) * Time.fixedDeltaTime; // Bullets are initially placed up to 1 frame ahead (iTime). Not offsetting by part vel gives the correct initial placement.
                missileRelativePosition = targetPosition - firePosition;
                float timeToCPA = timeToImpact; // Rough initial estimate.
                targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, timeToCPA);

                // Velocity Compensation Logic
                float compMult = Mathf.Clamp(0.5f * (targetDistance - termDist) / termDist, 0f, 1f);
                Vector3 velDirectionHor = (velDirection.ProjectOnPlanePreNormalized(upDirection)).normalized; //(velDirection - upDirection * Vector3.Dot(velDirection, upDirection)).normalized;
                targetHorVel = targetVelocity.ProjectOnPlanePreNormalized(upDirection); //targetVelocity - upDirection * Vector3.Dot(targetVelocity, upDirection); // Get target horizontal velocity (relative to missile frame)
                float targetAlVelMag = Vector3.Dot(targetHorVel, velDirectionHor); // Get magnitude of velocity aligned with the missile velocity vector (in the horizontal axis)
                targetAlVelMag *= Mathf.Sign(velComp) * compMult;
                targetAlVelMag = Mathf.Max(targetAlVelMag, 0f); //0.5f * (targetAlVelMag + Mathf.Abs(targetAlVelMag)); // Set -ve velocity (I.E. towards the missile) to 0 if velComp is +ve, otherwise for -ve

                float targetVertVelMag = Mathf.Max(0f, Mathf.Sign(vertVelComp) * compMult * Vector3.Dot(targetVelocity, upDirection));

                //targetCompVel = targetVelocity + velComp * targetHorVel.magnitude* targetHorVel.normalized; // Old velComp logic
                //targetCompVel = targetVelocity + velComp * targetAlVelMag * velDirectionHor; // New velComp logic
                targetCompVel = targetVelocity + velComp * targetAlVelMag * velDirectionHor + vertVelComp * targetVertVelMag * upDirection; // New velComp logic

                var count = 0;
                do
                {
                    lastVelDirection = velDirection;
                    currVel = currSpeed * velDirection;
                    //firePosition = missileVessel.transform.position + (currSpeed * velDirection) * Time.fixedDeltaTime; // Bullets are initially placed up to 1 frame ahead (iTime).
                    missileAcceleration = FlightGlobals.getGeeForceAtPosition((firePosition + targetPredictedPosition) / 2f); // Drag is ignored.
                    //bulletRelativePosition = targetPosition - firePosition + compMult * altComp * upDirection; // Compensate for altitude
                    missileRelativePosition = targetPosition - firePosition; // Compensate for altitude
                    missileRelativeVelocity = targetVelocity - currVel;
                    missileRelativeAcceleration = targetAcceleration - missileAcceleration;
                    timeToCPA = AIUtils.TimeToCPA(missileRelativePosition, missileRelativeVelocity, missileRelativeAcceleration, timeToImpact * 3f);
                    targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetCompVel, Vector3.zero, Mathf.Min(timeToCPA, 16f));
                    missileDropOffset = -0.5f * missileAcceleration * timeToCPA * timeToCPA;
                    ballisticTarget = targetPredictedPosition + missileDropOffset;
                    velDirection = (ballisticTarget - missileVessel.CoM).normalized;
                } while (++count < 10 && Vector3.Angle(lastVelDirection, velDirection) > 1f); // 1° margin of error is sufficient to prevent premature firing (usually)


                // Determine horizontal and up components of velocity, calculate the elevation angle
                float velUp = Vector3.Dot(velDirection, upDirection);
                float velForwards = (velDirection - upDirection * velUp).magnitude;
                float angle = Mathf.Atan2(velUp, velForwards);

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Loft Angle: [{(angle * Mathf.Rad2Deg):G3}]");

                // Use simple lead compensation to minimize over-compensation
                // Get planar direction to target
                Vector3 planarDirectionToTarget =
                    ((AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime + TimeWarp.fixedDeltaTime) - missileVessel.CoM).ProjectOnPlanePreNormalized(upDirection)).normalized;

                // Check if termination angle agrees with termAngle
                if ((loftState < MissileBase.LoftStates.Midcourse) && (angle > -termAngle * Mathf.Deg2Rad))
                {
                    /*// If not yet at termination, simple lead compensation
                    targetPosition += targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;

                    // Get planar direction to target
                    Vector3 planarDirectionToTarget = //(velDirection - upDirection * Vector3.Dot(velDirection, upDirection)).normalized;
                        ((targetPosition - missileVessel.transform.position).ProjectOnPlanePreNormalized(upDirection)).normalized;*/

                    // Altitude clamp based on rangeFactor and maxAlt, cannot be lower than target
                    float altitudeClamp = Mathf.Clamp(targetAlt + rangeFactor * Vector3.Dot(targetPosition - missileVessel.CoM, planarDirectionToTarget), targetAlt, Mathf.Max(maxAltitude, targetAlt));

                    // Old loft climb logic, wanted to limit turn. Didn't work well but leaving it in if I decide to fix it
                    /*if (missileVessel.altitude < (altitudeClamp - 0.5f))
                    //gain altitude if launching from stationary
                    {*/
                    //currSpeed = (float)missileVessel.Velocity().magnitude;

                    // 5g turn, v^2/r = a, v^2/(dh*(tan(45°/2)sin(45°))) > 5g, v^2/(tan(45°/2)sin(45°)) > 5g * dh, I.E. start turning when you need to pull a 5g turn,
                    // before that the required gs is lower, inversely proportional
                    /*if (loftState == 1 || (currSpeed * currSpeed * 0.2928932188134524755991556378951509607151640623115259634116f) >= (5f * (float)PhysicsGlobals.GravitationalAcceleration) * (altitudeClamp - missileVessel.altitude))
                    {*/
                    /*
                    loftState = 1;

                    // Calculate upwards and forwards velocity components
                    velUp = Vector3.Dot(missileVessel.Velocity(), upDirection);
                    velForwards = (float)(missileVessel.Velocity() - upDirection * velUp).magnitude;

                    // Derivation of relationship between dh and turn radius
                    // tan(theta/2) = dh/L, sin(theta) = L/r
                    // tan(theta/2) = sin(theta)/(1+cos(theta))
                    float turnR = (float)(altitudeClamp - missileVessel.altitude) * (currSpeed * currSpeed + currSpeed * velForwards) / (velUp * velUp);

                    float accel = Mathf.Clamp(currSpeed * currSpeed / turnR, 0, 5f * (float)PhysicsGlobals.GravitationalAcceleration);
                    */

                    // Limit climb angle by turnFactor, turnFactor goes negative when above target alt
                    float turnFactor = (float)(altitudeClamp - missileVessel.altitude) / (4f * (float)missileVessel.srfSpeed);
                    turnFactor = Mathf.Clamp(turnFactor, -1f, 1f);

                    loftAngle = Mathf.Max(loftAngle, angle);

                    gLimit = 6f;

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: AAM Loft altitudeClamp: [{altitudeClamp:G6}] COS: [{Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], SIN: [{Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad):G3}], turnFactor: [{turnFactor:G3}].");
                    return missileVessel.CoM + (float)missileVessel.srfSpeed * ((Mathf.Cos(loftAngle * turnFactor * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * turnFactor * Mathf.Deg2Rad) * upDirection));

                    /*
                    Vector3 newVel = (velForwards * planarDirectionToTarget + velUp * upDirection);
                    //Vector3 accVec = Vector3.Cross(newVel, Vector3.Cross(upDirection, planarDirectionToTarget));
                    Vector3 accVec = accel*(Vector3.Dot(newVel, planarDirectionToTarget) * upDirection - Vector3.Dot(newVel, upDirection) * planarDirectionToTarget).normalized;

                    return missileVessel.transform.position + 1.5f * Time.fixedDeltaTime * newVel + 2.25f * Time.fixedDeltaTime * Time.fixedDeltaTime * accVec;
                    */
                    /*}
                    return missileVessel.transform.position + 0.5f * (float)missileVessel.srfSpeed * ((Mathf.Cos(loftAngle * Mathf.Deg2Rad) * planarDirectionToTarget) + (Mathf.Sin(loftAngle * Mathf.Deg2Rad) * upDirection));
                    */
                    //}

                    //Vector3 finalTarget = missileVessel.transform.position + 0.5f * (float)missileVessel.srfSpeed * planarDirectionToTarget + ((altitudeClamp - (float)missileVessel.altitude) * upDirection.normalized);

                    //return finalTarget;
                }
                else
                {
                    loftState = MissileBase.LoftStates.Midcourse;

                    // Tried to do some kind of pro-nav method. Didn't work well, leaving it just in case I want to fix it.
                    /*
                    Vector3 newVel = (float)missileVessel.srfSpeed * velDirection;
                    Vector3 accVec = (newVel - missileVessel.Velocity());
                    Vector3 unitVel = missileVessel.Velocity().normalized;
                    accVec = accVec - unitVel * Vector3.Dot(unitVel, accVec);

                    float accelTime = Mathf.Clamp(timeToImpact, 0f, 4f);

                    accVec = accVec / accelTime;

                    float accel = accVec.magnitude;

                    if (accel > 20f * (float)PhysicsGlobals.GravitationalAcceleration)
                    {
                        accel = 20f * (float)PhysicsGlobals.GravitationalAcceleration / accel;
                    }
                    else
                    {
                        accel = 1f;
                    }

                    Debug.Log("[BDArmory.MissileGuidance]: Loft: Diving, accel = " + accel);
                    return missileVessel.transform.position + 1.5f * Time.fixedDeltaTime * missileVessel.Velocity() + 2.25f * Time.fixedDeltaTime * Time.fixedDeltaTime * accVec * accel;
                    */

                    Vector3 finalTargetPos;

                    if (velUp > 0f)
                    {
                        // If the missile is told to go up, then we either try to go above the target or remain at the current altitude
                        /*return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velDirection.x - upDirection.x * velUp,
                            velDirection.y - upDirection.y * velUp,
                            velDirection.z - upDirection.z * velUp) + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;*/
                        finalTargetPos = missileVessel.CoM + (float)missileVessel.srfSpeed * planarDirectionToTarget + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;
                    } else
                    {
                        // Otherwise just fly towards the target according to velUp and velForwards
                        float spdUp = (float)missileVessel.srfSpeed * velUp, spdF = (float)missileVessel.srfSpeed * velForwards;
                        finalTargetPos = new Vector3(missileVessel.CoM.x + spdUp * upDirection.x + spdF * planarDirectionToTarget.x,
                            missileVessel.CoM.y + spdUp * upDirection.y + spdF * planarDirectionToTarget.y,
                            missileVessel.CoM.z + spdUp * upDirection.z + spdF * planarDirectionToTarget.z);
                    }

                    // If the target is at <  2 * termDist start mixing
                    if (targetDistance < 2f * termDist)
                    {
                        float blendFac = (targetDistance - termDist) / termDist;

                        if (homingModeTerminal == MissileBase.GuidanceModes.PN)
                            return (1f - blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * finalTargetPos;
                        else if (homingModeTerminal == MissileBase.GuidanceModes.APN)
                            return (1f - blendFac) * GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact, out gLimit) + blendFac * finalTargetPos;
                        else if (homingModeTerminal == MissileBase.GuidanceModes.AAMPure)
                            return (1f - blendFac) * targetPosition + blendFac * finalTargetPos;
                        else
                            return (1f - blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * finalTargetPos; // Default to PN
                    }

                    // No mixing if targetDistance > 2 * termDist
                    return finalTargetPos;

                    //if (velUp > 0f)
                    //{
                    //    // If the missile is told to go up, then we either try to go above the target or remain at the current altitude
                    //    /*return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velDirection.x - upDirection.x * velUp,
                    //        velDirection.y - upDirection.y * velUp,
                    //        velDirection.z - upDirection.z * velUp) + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;*/
                    //    return missileVessel.transform.position + (float)missileVessel.srfSpeed * planarDirectionToTarget + Mathf.Max(targetAlt - (float)missileVessel.altitude, 0f) * upDirection;
                    //}

                    //// Otherwise just fly towards the target according to velUp and velForwards
                    ////return missileVessel.transform.position + (float)missileVessel.srfSpeed *  velDirection;
                    //return missileVessel.transform.position + (float)missileVessel.srfSpeed * new Vector3(velUp * upDirection.x + velForwards * planarDirectionToTarget.x,
                    //    velUp * upDirection.y + velForwards * planarDirectionToTarget.y,
                    //    velUp * upDirection.z + velForwards * planarDirectionToTarget.z);
                }
            }
            else
            {
                // If terminal just go straight for target + lead
                loftState = MissileBase.LoftStates.Terminal;
                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Terminal");

                if (targetDistance < 2f * termDist)
                {
                    float blendFac = 0f;
                    Vector3 targetPos = Vector3.zero;

                    if ((targetDistance > termDist) && (homingModeTerminal != MissileBase.GuidanceModes.AAMLead) && (homingModeTerminal != MissileBase.GuidanceModes.AAMPure))
                    {
                        blendFac = (targetDistance - termDist) / termDist;
                        targetPos = AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime + TimeWarp.fixedDeltaTime);
                    }
                        

                    if (homingModeTerminal == MissileBase.GuidanceModes.PN)
                        return (1f-blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * targetPos;
                    else if (homingModeTerminal == MissileBase.GuidanceModes.APN)
                        return (1f - blendFac) * GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact, out gLimit) + blendFac * targetPos;
                    else if (homingModeTerminal == MissileBase.GuidanceModes.AAMLead)
                        return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime);
                    else if (homingModeTerminal == MissileBase.GuidanceModes.AAMPure)
                        return targetPosition;
                    else
                        return (1f - blendFac) * GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact, out gLimit) + blendFac * targetPos; // Default to PN
                }
                else
                {
                    return AIUtils.PredictPosition(targetPosition, targetVelocity, Vector3.zero, leadTime + TimeWarp.fixedDeltaTime); //targetPosition + targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;
                    //return targetPosition + targetVelocity * leadTime;
                }
            }
        }

/*        public static Vector3 GetAirToAirHybridTarget(Vector3 targetPosition, Vector3 targetVelocity,
            Vector3 targetAcceleration, Vessel missileVessel, float termDist, out float timeToImpact,
            MissileBase.GuidanceModes homingModeTerminal, float N, float minSpeed = 200)
        {
            Vector3 velDirection = missileVessel.srf_vel_direction; //missileVessel.Velocity().normalized;

            float targetDistance = Vector3.Distance(targetPosition, missileVessel.transform.position);

            float currSpeed = Mathf.Max((float)missileVessel.srfSpeed, minSpeed);
            Vector3 currVel = currSpeed * velDirection;

            float leadTime = targetDistance / (targetVelocity - currVel).magnitude;

            timeToImpact = leadTime;
            leadTime = Mathf.Clamp(leadTime, 0f, 8f);

            if (targetDistance < termDist)
            {
                if (homingModeTerminal == MissileBase.GuidanceModes.APN)
                    return GetAPNTarget(targetPosition, targetVelocity, targetAcceleration, missileVessel, N, out timeToImpact);
                else if (homingModeTerminal == MissileBase.GuidanceModes.PN)
                    return GetPNTarget(targetPosition, targetVelocity, missileVessel, N, out timeToImpact);
                else if (homingModeTerminal == MissileBase.GuidanceModes.AAMPure)
                    return targetPosition;
                else
                    return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime);
            }
            else
            {
                return AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, leadTime + TimeWarp.fixedDeltaTime); //targetPosition + targetVelocity * leadTime + 0.5f * leadTime * leadTime * targetAcceleration;
                                                                                                                                        //return targetPosition + targetVelocity * leadTime;
            }
        }*/

        public static Vector3 GetAirToAirTargetModular(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, out float timeToImpact)
        {
            float targetDistance = Vector3.Distance(targetPosition, missileVessel.CoM);

            //Basic lead time calculation
            Vector3 currVel = missileVessel.Velocity();
            timeToImpact = targetDistance / (targetVelocity - currVel).magnitude;

            // Calculate time to CPA to determine target position
            float timeToCPA = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 16f);
            timeToImpact = (timeToCPA < 16f) ? timeToCPA : timeToImpact;
            // Ease in velocity from 16s to 8s, ease in acceleration from 8s to 2s using the logistic function to give smooth adjustments to target point.
            float easeAccel = Mathf.Clamp01(1.1f / (1f + Mathf.Exp((timeToCPA - 5f))) - 0.05f);
            float easeVel = Mathf.Clamp01(2f - timeToCPA / 8f);
            return AIUtils.PredictPosition(targetPosition, targetVelocity * easeVel, targetAcceleration * easeAccel, timeToCPA + TimeWarp.fixedDeltaTime); // Compensate for the off-by-one frame issue.
        }

        public static Vector3 GetPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float N, out float timeToGo, out float gLimit)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, Vector3.zero, 120f);
            return missileVessel.CoM + missileVel * timeToGo + normalAccel * timeToGo * timeToGo;
        }

        private static Vector3 GetPNAccel(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel, float N)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            //gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            //timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, Vector3.zero, 120f);
            return normalAccel;
        }

        public static Vector3 GetAPNTarget(Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, Vessel missileVessel, float N, out float timeToGo, out float gLimit)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 RefVector = missileVel.normalized;
            Vector3 normalAccel = -N * relVelocity.magnitude * Vector3.Cross(RefVector, RotVector);
            // float tgo = relRange.magnitude / relVelocity.magnitude;
            Vector3 accelBias = Vector3.Cross(relRange.normalized, targetAcceleration);
            accelBias = Vector3.Cross(RefVector, accelBias);
            normalAccel -= 0.5f * N * accelBias;
            gLimit = normalAccel.magnitude / (float)PhysicsGlobals.GravitationalAcceleration;
            timeToGo = missileVessel.TimeToCPA(targetPosition, targetVelocity, targetAcceleration, 120f);
            return missileVessel.CoM + missileVel * timeToGo + normalAccel * timeToGo * timeToGo;
        }
        public static float GetLOSRate(Vector3 targetPosition, Vector3 targetVelocity, Vessel missileVessel)
        {
            Vector3 missileVel = missileVessel.Velocity();
            Vector3 relVelocity = targetVelocity - missileVel;
            Vector3 relRange = targetPosition - missileVessel.CoM;
            Vector3 RotVector = Vector3.Cross(relRange, relVelocity) / Vector3.Dot(relRange, relRange);
            Vector3 LOSRate = Mathf.Rad2Deg * RotVector;
            return LOSRate.magnitude;
        }

        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vessel targetVessel)
        {
            float temp;
            return GetAirToAirFireSolution(missile, targetVessel, out temp);
        }

        /// <summary>
        /// Air-2-Air fire solution used by the AI for steering, WM checking if a missile can be launched, unguided missiles
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetVessel"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vessel targetVessel, out float timetogo)
        {
            if (!targetVessel)
            {
                timetogo = float.PositiveInfinity;
                return missile.vessel.CoM + (missile.GetForwardTransform() * 1000);
            }
            Vector3 targetPosition = targetVessel.CoM;
            Vector3 vel = missile.vessel.Velocity();
            Vector3 startPosition = missile.vessel.CoM;
            if (missile.GetWeaponClass() == WeaponClasses.SLW && !missile.vessel.LandedOrSplashed)
            {
                vel = Vector3.zero;  //impact w/ water is going to bring starting torp speed basically down to 0, not whatever plane airspeed was
                float torpDropTime = BDAMath.Sqrt(2 * (float)missile.vessel.altitude / (float)FlightGlobals.getGeeForceAtPosition(missile.vessel.CoM).magnitude);
                startPosition += missile.vessel.srf_vel_direction * (missile.vessel.horizontalSrfSpeed * torpDropTime); //torp will spend multiple seconds dropping falling at parent vessel speed
                startPosition -= (float)FlightGlobals.getAltitudeAtPos(startPosition) * missile.vessel.up;
                targetPosition += targetVessel.Velocity() * torpDropTime; //so offset start positions appropriately
            }
            float leadTime = 0;
            float targetDistance = Vector3.Distance(targetPosition, startPosition);

            MissileLauncher launcher = missile as MissileLauncher;
            BDModularGuidance modLauncher = missile as BDModularGuidance;
    
            float accel = launcher != null ? (launcher.thrust / missile.part.mass) : modLauncher != null ? (modLauncher.thrust/modLauncher.mass) : 10;
            
            if (missile.vessel.InNearVacuum() && missile.vessel.InOrbit()) // In orbit, use orbital calc
            {
                float timeToImpact;
                Vector3 relPos = targetVessel.CoM - missile.vessel.CoM;
                Vector3 relVel = vel - targetVessel.Velocity();
                Vector3 relAccel = targetVessel.acceleration_immediate - missile.GetForwardTransform() * accel;

                float thrustTime = launcher != null ? launcher.boostTime : modLauncher != null ? (modLauncher.MaxSpeed / accel) : 8;

                timeToImpact = AIUtils.TimeToCPA(relPos, relVel, relAccel, thrustTime);
                if (timeToImpact == thrustTime)
                {
                    relPos = AIUtils.PredictPosition(targetPosition, targetVessel.Velocity(), targetVessel.acceleration_immediate, thrustTime) -
                    AIUtils.PredictPosition(missile.vessel.CoM, vel, missile.GetForwardTransform() * accel, thrustTime);
                    relVel += relAccel * timeToImpact;
                    relAccel = targetVessel.acceleration_immediate;
                    timeToImpact = AIUtils.TimeToCPA(relPos, relVel, relAccel, 60f);
                    leadTime = thrustTime + timeToImpact;   
                }
                else
                    leadTime = timeToImpact;
                targetPosition += leadTime * (targetVessel.Velocity() - vel) + 0.5f * leadTime * leadTime * targetVessel.acceleration_immediate;
            }
            else // In atmo, use in-atmo calculations
            {
                Vector3 VelOpt = missile.GetForwardTransform() * (launcher != null ? launcher.optimumAirspeed : 1500);
                Vector3 deltaVel = targetVessel.Velocity() - vel;
                Vector3 DeltaOptvel = targetVessel.Velocity() - VelOpt;
                float T = Mathf.Clamp(Vector3.Project(VelOpt - vel, missile.GetForwardTransform()).magnitude / accel, 0, 8); //time to optimal airspeed

                Vector3 relPosition = targetPosition - startPosition;
                Vector3 relAcceleration = targetVessel.acceleration_immediate - missile.GetForwardTransform() * accel;
                leadTime = AIUtils.TimeToCPA(relPosition, deltaVel, relAcceleration, T); //missile accelerating, T is greater than our max look time of 8s
                if (T < 8 && leadTime == T)//missile has reached max speed, and is now cruising; sim positions ahead based on T and run CPA from there
                {
                    relPosition = AIUtils.PredictPosition(targetPosition, targetVessel.Velocity(), targetVessel.acceleration_immediate, T) -
                        AIUtils.PredictPosition(startPosition, vel, missile.GetForwardTransform() * accel, T);
                    relAcceleration = targetVessel.acceleration_immediate; // - missile.MissileReferenceTransform.forward * 0; assume missile is holding steady velocity at optimumAirspeed
                    leadTime = AIUtils.TimeToCPA(relPosition, DeltaOptvel, relAcceleration, 8 - T) + T;
                }

                targetPosition += leadTime * targetVessel.Velocity();

                if (targetVessel && targetDistance < 800) //TODO - investigate if this would throw off aim accuracy
                {
                    targetPosition += (Vector3)targetVessel.acceleration_immediate * 0.05f * leadTime * leadTime;
                }
            }

            timetogo = leadTime;
            return targetPosition;
        }
        /// <summary>
        /// Air-2-Air lead offset calcualtion used for aiming missile turrets
        /// </summary>
        /// <param name="missile"></param>
        /// <param name="targetPosition"></param>
        /// <param name="targetVelocity"></param>
        /// <returns></returns>
        public static Vector3 GetAirToAirFireSolution(MissileBase missile, Vector3 targetPosition, Vector3 targetVelocity)
        {
            MissileLauncher launcher = missile as MissileLauncher;
            BDModularGuidance modLauncher = missile as BDModularGuidance;
            bool inSpace = missile.vessel.InNearVacuum() && missile.vessel.InOrbit();
            float leadTime = 0;
            float maxSimTime = 8f;
            Vector3 leadPosition = targetPosition;
            Vector3 vel = missile.vessel.Velocity();
            Vector3 leadDirection, velOpt;
            float accel = launcher != null ? ((Mathf.Clamp01(launcher.boostTime / maxSimTime)) * launcher.thrust + Mathf.Clamp01((maxSimTime - launcher.cruiseDelay - launcher.boostTime) / maxSimTime) * launcher.cruiseThrust) / missile.part.mass
                : modLauncher.thrust / modLauncher.mass;
            float leadTimeError = 1f;
                int count = 0;
                do
                {
                    leadDirection = leadPosition - missile.vessel.CoM;
                    float targetDistance = leadDirection.magnitude;
                    leadDirection.Normalize();
                    velOpt = (inSpace ? BDAMath.Sqrt(2 * accel * targetDistance) * leadDirection + vel : (launcher != null ? launcher.optimumAirspeed : 1500) * leadDirection);
                    float deltaVel = Vector3.Dot(targetVelocity - vel, leadDirection);
                    float deltaVelOpt = Vector3.Dot(targetVelocity - velOpt, leadDirection);
                    float T = Mathf.Clamp((velOpt - vel).magnitude / accel, 0, maxSimTime); //time to optimal airspeed, clamped to at most 8s
                    float D = deltaVel * T + 1 / 2 * accel * (T * T); //relative distance covered accelerating to optimum airspeed
                    leadTimeError = -leadTime;
                    if (targetDistance > D) leadTime = (targetDistance - D) / deltaVelOpt + T;
                    else leadTime = (-deltaVel - BDAMath.Sqrt((deltaVel * deltaVel) + 2 * accel * targetDistance)) / accel;
                    leadTime = Mathf.Clamp(leadTime, 0f, maxSimTime);
                    leadTimeError += leadTime;
                    leadPosition = AIUtils.PredictPosition(targetPosition, targetVelocity - (inSpace ? vel : Vector3.zero), Vector3.zero, leadTime);
                } while (++count < 5 && Mathf.Abs(leadTimeError) > 1e-3f);  // At most 5 iterations to converge. Also, 1e-2f may be sufficient.
            return leadPosition;
        }

        public static Vector3 GetCruiseTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = missileVessel.upAxis; //VectorUtils.GetUpDirection(missileVessel.transform.position);
            float currentRadarAlt = GetRadarAltitude(missileVessel);
            float distanceSqr =
                (targetPosition - (missileVessel.CoM - (currentRadarAlt * upDirection))).sqrMagnitude;

            Vector3 planarDirectionToTarget = (targetPosition - missileVessel.CoM).ProjectOnPlanePreNormalized(upDirection).normalized;

            float error;

            if (currentRadarAlt > 1600)
            {
                error = 500000;
            }
            else
            {
                Vector3 tRayDirection = (planarDirectionToTarget * 10) - (10 * upDirection);
                Ray terrainRay = new Ray(missileVessel.CoM, tRayDirection);
                RaycastHit rayHit;

                if (Physics.Raycast(terrainRay, out rayHit, 8000, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
                {
                    float detectedAlt =
                        Vector3.Project(rayHit.point - missileVessel.CoM, upDirection).magnitude;

                    error = Mathf.Min(detectedAlt, currentRadarAlt) - radarAlt;
                }
                else
                {
                    error = currentRadarAlt - radarAlt;
                }
            }

            error = Mathf.Clamp(0.05f * error, -5, 3);
            return missileVessel.CoM + (10 * planarDirectionToTarget) - (error * upDirection);
        }

        public static Vector3 GetTerminalManeuveringTarget(Vector3 targetPosition, Vessel missileVessel, float radarAlt)
        {
            Vector3 upDirection = missileVessel.upAxis;
            Vector3 planarVectorToTarget = (targetPosition - missileVessel.CoM).ProjectOnPlanePreNormalized(upDirection);
            Vector3 planarDirectionToTarget = planarVectorToTarget.normalized;
            Vector3 crossAxis = Vector3.Cross(planarDirectionToTarget, upDirection).normalized;
            float sinAmplitude = Mathf.Clamp(Vector3.Distance(targetPosition, missileVessel.CoM) - 850, 0,
                4500);
            Vector3 sinOffset = (Mathf.Sin(1.25f * Time.time) * sinAmplitude * crossAxis);
            Vector3 targetSin = targetPosition + sinOffset;
            Vector3 planarSin = missileVessel.CoM + planarVectorToTarget + sinOffset;

            Vector3 finalTarget;
            float finalDistance = 2500 + GetRadarAltitude(missileVessel);
            if ((targetPosition - missileVessel.CoM).sqrMagnitude > finalDistance * finalDistance)
            {
                finalTarget = targetPosition;
            }
            else if (!GetBallisticGuidanceTarget(targetSin, missileVessel, true, out finalTarget))
            {
                //finalTarget = GetAirToGroundTarget(targetSin, missileVessel, 6);
                finalTarget = planarSin;
            }
            return finalTarget;
        }

        public static FloatCurve DefaultLiftCurve = new([
            new(0, 0),
            new(8, 0.35f),
            //new(19, 1f),
            //new(23, 0.9f),
            new(30, 1.5f),
            new(65, 0.6f),
            new(90, 0.7f)
            ]);
      
        public static FloatCurve DefaultDragCurve = new([
            new(0, 0.00215f, 0.00014f, 0.00014f),
            new(5, .00285f, 0.0002775f, 0.0002775f),
            new(15, .007f, 0.0003146428f, 0.0003146428f),
            new(29, .01f, 0.0002142857f, 0.01115385f),
            new(55, .3f, 0.008434067f, 0.008434067f),
            new(90, .5f, 0.005714285f, 0.005714285f)
        ]);
      
        // The below curves and constants are derived from the lift and drag curves and will need to be re-calculated
        // if these are changed

        const float TRatioInflec1 = 1.181181181181181f; // Thrust to Lift Ratio (at AoA of 30) where the maximum occurs
        // after the 65 degree mark
        const float TRatioInflec2 = 2.242242242242242f; // Thrust to Lift Ratio (at AoA of 30) where a local maximum no
        // longer exists, above this every section must be searched

        public static FloatCurve AoACurve = new([
            new(0.0000000000f, 30.0000000000f),
            new(0.7107107107f, 33.9639639640f),
            new(1.5315315315f, 39.6396396396f),
            new(1.9419419419f, 43.6936936937f),
            new(2.1421421421f, 46.6666666667f),
            new(2.2122122122f, 48.3783783784f),
            new(2.2422422422f, 49.7297297297f)
        ]); // Floatcurve containing AoA of (local) max acceleration
        // for a given thrust to lift (at the max CL of 1.5 at 30 degrees of AoA) ratio. Limited to a max
        // of TRatioInflec2 where a local maximum no longer exists

        public static FloatCurve AoAEqCurve = new([
            new(1.1911911912f, 89.6396396396f),
            new(1.3413413413f, 81.6216216216f),
            new(1.5215215215f, 73.3333333333f),
            new(1.7217217217f, 67.4774774775f),
            new(1.9819819820f, 62.4324324324f),
            new(2.1821821822f, 56.6666666667f),
            new(2.2422422422f, 52.6126126126f)
        ]); // Floatcurve containing AoA after which the acceleration goes above
        // that of the local maximums'. Only exists between TRatioInflec1 and TRatioInflec2.

        public static FloatCurve gMaxCurve = new([
            new(0.0000000000f, 1.5000000000f),
            new(1.2012012012f, 2.4907813293f),
            new(1.9119119119f, 3.1757276995f),
            new(2.2422422422f, 3.5307206802f)
        ]); // Floatcurve containing max acceleration times the mass (total force)
        // normalized by q*S*GLOBAL_LIFT_MULTIPLIER for TRatio between 0 and TRatioInflec2. Note that after TRatioInflec1
        // this becomes a local maxima not a global maxima. This is used to narrow down what part of the curve we should
        // solve on.

        // Linearized CL v.s. AoA curve to enable fast solving. Algorithm performs bisection using the fast calculations of the bounds
        // and then performs a linear solve 
        public static float[] linAoA = { 0f, 10f, 24f, 30f, 38f, 57f, 65f, 90f };
        public static float[] linCL = { 0f, 0.454444597111092f, 1.34596044049850f, 1.5f, 1.38043381924198f, 0.719566180758018f, 0.6f, 0.7f };
        // Sin at the points
        public static float[] linSin = { 0f, 0.173648177666930f, 0.406736643075800f, 0.5f, 0.615661475325658f, 0.838670567945424f, 0.906307787036650f, 1f };
        // Slope of CL at the intervals
        public static float[] linSlope = { 0.0454444597111092f, 0.0636797030991005f, 0.0256732599169169f, -0.0149457725947522f, -0.0347825072886297f, -0.0149457725947522f, 0.004f };
        // y-Intercept of line at those intervals
        public static float[] linIntc = { 0f, -0.182352433879912f, 0.729802202492494f, 1.94837317784257f, 2.70216909620991f, 1.57147521865889f, 0.34f };

        public static float getGLimit(MissileLauncher ml, float thrust, float gLim, float margin)//, out bool gLimited)
        {
            if (ml.vessel.InVacuum())
            {
                return 180f; // if in vacuum g-limiting should be done via throttle modulation
            }
            
            bool gLimited = false;

            // Force required to reach g-limit
            gLim *= (float)(ml.vessel.totalMass * PhysicsGlobals.GravitationalAcceleration);

            float maxAoA = ml.maxAoA;

            float currAoA = maxAoA;

            int interval = 0;

            // Factor by which to multiply the lift coefficient to get lift, it's the dynamic pressure times the lift area times
            // the global lift multiplier
            float qSk = (float) (0.5f * ml.vessel.atmDensity * ml.vessel.srfSpeed * ml.vessel.srfSpeed) * ml.liftArea * BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;

            float currG = 0;

            // If we're in the post thrust state
            if (thrust == 0)
            {
                // If the maximum lift achievable is not enough to reach the request accel
                // the we turn to the AoA required for max lift
                if (gLim > 1.5f*qSk)
                {
                    currAoA = 30f;
                }
                else
                {
                    // Otherwise, first we calculate the lift in interval 2 (between 24 and 30 AoA)
                    currG = linCL[2] * qSk; // CL(alpha)*qSk + thrust*sin(alpha)

                    // If the resultant g at 24 AoA is < gLim then we're in interval 2
                    if (currG < gLim)
                    {
                        interval = 2;
                    }
                    else
                    {
                        // Otherwise check interval 1
                        currG = linCL[1] * qSk;
                        
                        if (currG > gLim)
                        {
                            // If we're still > gLim then we're in interval 0
                            interval = 0;
                        }
                        else
                        {
                            // Otherwise we're in interval 1
                            interval = 1;
                        }
                    }

                    // Calculate AoA for G, since no thrust we can use the faster linear equation
                    currAoA = calcAoAforGLinear(qSk, gLim, linSlope[interval], linIntc[interval], 0);
                }

                // Are we gLimited?
                gLimited = currAoA < maxAoA;
                return gLimited ? currAoA : maxAoA;
            }
            else
            {
                // If we're under thrust, first calculate the ratio of Thrust to lift at max CL
                float TRatio = thrust / (1.5f * qSk);

                // Initialize bisection limits
                int LHS = 0;
                int RHS = 7;

                if (TRatio < TRatioInflec2)
                {
                    // If we're below TRatioInflec2 then we know there's a local max
                    currG = gMaxCurve.Evaluate(TRatio);

                    if (TRatio > TRatioInflec1)
                    {
                        // If we're above TRatioInflec1 then we know it's only a local max

                        // First calculate the allowable force margin
                        // This exists because drag gets very bad above the local max
                        margin = Mathf.Max(margin, 0f);
                        margin *= (float)ml.vessel.totalMass;

                        if (currG + margin < gLim)
                        {
                            // If we're within the margin
                            if (currG > gLim)
                            {
                                // And our local max is > gLim, then we know that 
                                // there is a solution. Calculate the AoAMax
                                // where the local max occurs
                                float AoAMax = AoACurve.Evaluate(TRatio);
                                
                                // And determine our right hand bound based on
                                // our AoAMax
                                if (AoAMax > linAoA[4])
                                {
                                    RHS = 5;
                                }
                                else if (AoAMax > linAoA[3])
                                {
                                    RHS = 4;
                                }
                                else
                                {
                                    RHS = 3;
                                }
                            }
                            else
                            {
                                // If our local max is < gLim then we can simply set
                                // our AoA to be the AoA of the local max
                                currAoA = AoACurve.Evaluate(TRatio);
                                gLimited = currAoA < maxAoA;
                                return gLimited ? currAoA : maxAoA;
                            }
                        }
                        else
                        {
                            // If we're not within the margin then we need to consider
                            // the high AoA section. First calculate the absolute maximum
                            // g we can achieve
                            currG = 0.3f * qSk + thrust;

                            // If the absolute maximum g we can achieve is not enough, then return
                            // the local maximum in order to preserve energy
                            if (currG < gLim)
                            {
                                currAoA = AoACurve.Evaluate(TRatio);
                                gLimited = currAoA < maxAoA;
                                return gLimited ? currAoA : maxAoA;
                            }

                            // If we're within the limit, then find the AoA where the normal force
                            // once again reaches the local max value
                            float AoAEq = AoAEqCurve.Evaluate(TRatio);

                            // And determine the left hand bound from there
                            if (AoAEq > linAoA[7])
                            {
                                // If we're in the final section then just calculate it directly
                                currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[7], linIntc[7], 0);
                                gLimited = currAoA < maxAoA;
                                return gLimited ? currAoA : maxAoA;
                            }
                            else if (AoAEq > linAoA[6])
                            {
                                LHS = 6;
                            }
                            else if (AoAEq > linAoA[5])
                            {
                                LHS = 5;
                            }
                            else
                            {
                                LHS = 4;
                            }
                        }
                    }
                    else
                    {
                        // If we're not above TRatioInflec1 then we only have to consider the
                        // curve up to the local max
                        float AoAMax = AoACurve.Evaluate(TRatio);

                        // Determine the right hand bound for calculation
                        if (currG < gLim)
                        {
                            if (AoAMax > linAoA[3])
                            {
                                RHS = 4;
                            }
                            else
                            {
                                RHS = 3;
                            }
                        }
                        else
                        {
                            gLimited = currAoA < maxAoA;
                            return gLimited ? currAoA : maxAoA;
                        }
                    }
                }
                // If we're above TRatioInflec2 then we have to search the whole thing, but past that ratio
                // the function is monotonically increasing so it's OK

                // Bisection search
                while ( (RHS - LHS) > 1)
                {
                    interval = (int)(0.5f * (RHS + LHS));

                    currG = linCL[interval] * qSk + thrust * linSin[interval];

                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: LHS: {LHS}, RHS: {RHS}, interval: {interval}, currG: {currG}, gLim: {gLim}");

                    if (currG < gLim)
                    {
                        LHS = interval;
                    }
                    else
                    {
                        RHS = interval;
                    }
                }

                if (LHS < 2)
                {
                    // If we're below 15 (here 10 degrees) then use the linear approximation for sin
                    currAoA = calcAoAforGLinear(qSk, gLim, linSlope[LHS], linIntc[LHS], thrust);
                }
                else
                {
                    // Otherwise use the second order approximation centered at pi/2
                    currAoA = calcAoAforGNonLin(qSk, gLim, linSlope[LHS], linIntc[LHS], thrust);
                }

                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileGuidance]: Final Interval: {LHS}, currAoA: {currAoA}, gLim: {gLim}");

                gLimited = currAoA < maxAoA;
                return gLimited ? currAoA : maxAoA;
            }
            // Pseudocode / logic
            // If T = 0
            // We know it's in the first section. If m*gReq > (1.5*q*k*s) then set to min of maxAoA and 30 (margin?). If
            // < then we first make linear estimate, then solve by bisection of intervals first -> solve on interval.
            // If TRatio < TRatioInflec2
            // First we check the endpoints -> both gMax, and, if TRatio > TRatioInflec1, then 0.3*q*S*k + T (90 degree case).
            // If gMax > m*gReq then the answer < AoACurve -> Determine where it is via calculating the pre-calculated points
            // then seeing which one has gCalc > m*gReq, using the interval bounded by the point with gCalc > m*gReq on the
            // right end. Use bisection -> we know it's bounded at the RHS by the 38 or the 57 section. We can compare the
            // AoACurve with 38, if > 38 then use 57 as the bound, otherwise bisection with 38 as the bound. Using this to
            // determine which interval we're looking at, we then calc AoACalc. Return the min of maxAoA and AoACalc.
            // If gMax < m*gReq, then if TRatio < TRatioInflec1, set to min of AoACurve and maxAoA. If TRatio > TRatioInflec1
            // then we look at the 0.3*q*S*k + T. If < m*gReq then we'll set it to the min of maxAoA and either AoACurve or
            // 90, depends on the margin. See below. If > m*gReq then it's in the last two sections, bound by AoAEq on the LHS.
            // If AoAEq > 65, then we solve on the last section. If AoAEq < 65 then we check the point at AoA = 65 using the
            // pre-calculated values. If > m*gReq then we know that it's in the 57-65 section, otherwise we know it's in the
            // 65-90 section.
            // Consider adding a margin, if gMax only misses m*gReq by a little we should probably avoid going to the higher
            // angles as it adds a lot of drag. Maybe distance based? User settable?
            // If TRatio > TRatioInflec2 then we have a continuously monotonically increasing function
            // We use the fraction m*gReq/(0.3*q*S*k + T) to determine along which interval we should solve, noting that this
            // is an underestimate of the thrust required. (Maybe use arcsin for a more accurate estimate? Costly.) Then simply
            // calculate the pre-calculated value at the next point -> bisection and solve on the interval.
            
            // For all cases, if AoA < 15 then we can use the linear approximation of sin, if an interval includes both AoA < 15
            // and AoA > 15 then try < 15 (interval 2) first, then if > 15 try the non-linear starting from 15. Otherwise we use
            // non-linear equation.
        }

        // Calculate AoA for a given g loading, given m*g, the dynamic pressure times the lift area times the lift multiplier,
        // the linearized approximation of the AoA curve (in slope, y-intercept form) and the thrust. Linear uses a linear
        // small angle approximation for sin and non-linear uses a 2nd order approximation of sin about pi/2
        public static float calcAoAforGLinear(float qSk, float mg, float CLalpha, float CLintc, float thrust)
        {
            return Mathf.Rad2Deg * (mg - CLintc * qSk) / (CLalpha * qSk + thrust);
        }

        public static float calcAoAforGNonLin(float qSk, float mg, float CLalpha, float CLintc, float thrust)
        {
            CLalpha *= qSk;

            return Mathf.Rad2Deg * (2f * CLalpha + Mathf.PI * thrust + 2f * BDAMath.Sqrt(CLalpha * CLalpha + Mathf.PI * thrust * CLalpha + 2f * thrust * (CLintc * qSk + thrust - mg))) / (2f * thrust);
        }

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float dragArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA)
        {

            FloatCurve liftCurve = DefaultLiftCurve;
            FloatCurve dragCurve = DefaultDragCurve;

            return DoAeroForces(ml, targetPosition, liftArea, dragArea, steerMult, previousTorque, maxTorque, maxAoA, liftCurve,
                dragCurve);
        }

        public static Vector3 DoAeroForces(MissileLauncher ml, Vector3 targetPosition, float liftArea, float dragArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA, FloatCurve liftCurve, FloatCurve dragCurve)
        {
            Rigidbody rb = ml.part.rb;
            if (rb == null || rb.mass == 0) return Vector3.zero;
            double airDensity = ml.vessel.atmDensity;
            double airSpeed = ml.vessel.srfSpeed;
            Vector3d velocity = ml.vessel.Velocity();
            Vector3d velNorm = velocity.normalized;
            Vector3 forward = ml.transform.forward;

            //temp values
            Vector3 CoL = new Vector3(0, 0, -1f);
            float liftMultiplier = BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            float dragMultiplier = BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;

            //lift
            float AoA = Mathf.Clamp(VectorUtils.AnglePreNormalized(forward, velNorm), 0, 90);
            Vector3 forcePos = ml.transform.TransformPoint(ml.part.CoMOffset + CoL);
            Vector3 forceDirection = -velocity.ProjectOnPlanePreNormalized(forward).normalized;
            double liftForce = 0.0;
            ml.smoothedAoA.Update(AoA);
            if (AoA > 0)
            {
                liftForce = 0.5 * airDensity * airSpeed * airSpeed * liftArea * liftMultiplier * Mathf.Max(liftCurve.Evaluate(AoA), 0f);
                rb.AddForceAtPosition((float)liftForce * forceDirection,
                    forcePos);
            }

            //drag
            double dragForce = 0.0;
            if (airSpeed > 0)
            {
                dragForce = 0.5 * airDensity * airSpeed * airSpeed * dragArea * dragMultiplier * Mathf.Max(dragCurve.Evaluate(AoA), 0f);
                rb.AddForceAtPosition((float)dragForce * -velNorm,
                    forcePos);
            }

            //guidance
            if (airSpeed > 1f || (ml.vacuumSteerable && ml.Throttle > 0))
            {
                /* This is what the torque on the missile due to aero forces would be
                Vector3 aeroTorque = Vector3.Cross(forcePos - ml.vessel.CoM,
                    new Vector3d(liftForce * forceDirection.x - dragForce * velNorm.x,
                                 liftForce * forceDirection.y - dragForce * velNorm.y,
                                 liftForce * forceDirection.z - dragForce * velNorm.z));
                */
                // So instead we take the opposite cross product to get the negative/opposing torque
                Vector3 aeroTorque = Vector3.Cross(new Vector3d(liftForce * forceDirection.x - dragForce * velNorm.x,
                                                                liftForce * forceDirection.y - dragForce * velNorm.y,
                                                                liftForce * forceDirection.z - dragForce * velNorm.z),
                                                                forcePos - ml.vessel.CoM);
                //Debug.Log($"[BDArmory.MissileGuidance]: aeroTorque = {aeroTorque}.");
                /* Legacy Missile Controller
                Vector3 targetDirection; // = (targetPosition - ml.transform.position);
                float targetAngle;
                if (AoA < maxAoA)
                {
                    targetDirection = (targetPosition - ml.vessel.CoM);
                    targetAngle = Mathf.Min(maxAoA,Vector3.Angle(velNorm, targetDirection) * 4f);
                }
                else
                {
                    targetDirection = velNorm;
                    targetAngle = 0f; //AoA;
                }

                Vector3 torqueDirection = -Vector3.Cross(targetDirection, velNorm).normalized;
                torqueDirection = ml.transform.InverseTransformDirection(torqueDirection);

                float torque = Mathf.Clamp(targetAngle * steerMult, 0, maxTorque);
                Vector3 finalTorque = Vector3.Lerp(previousTorque, torqueDirection * torque, 1).ProjectOnPlanePreNormalized(Vector3.forward);
                */

                Vector3 targetDirection = (targetPosition - ml.vessel.CoM).normalized;
                float targetAngle = VectorUtils.AnglePreNormalized(velNorm, targetDirection);
                if (targetAngle > maxAoA)
                    targetDirection = Vector3.Slerp(velNorm, targetDirection, maxAoA / targetAngle);
                float turningAngle = VectorUtils.AnglePreNormalized(forward, targetDirection);
                Vector3 finalTorque;
                if (turningAngle > 0f)
                {
                    Vector3 torqueDirection = Vector3.Cross(forward, targetDirection) / Mathf.Sin(turningAngle * Mathf.Deg2Rad);
                    //Debug.Log($"[BDArmory.MissileGuidance]: torqueDirection = {torqueDirection}, sqrMagnitude = {torqueDirection.sqrMagnitude}.");
                    float torque = Mathf.Clamp(turningAngle * 2f * steerMult, 0f, maxTorque);

                    // If aeroTorque < maxTorque we're not yet saturated
                    if (aeroTorque.sqrMagnitude < maxTorque * maxTorque)
                    {
                        //Debug.Log($"[BDArmory.MissileGuidance]: aeroTorque not saturated, torque = {torque}.");
                        // If torque drives us over maxTorque, then using the quadratic formula, we determine the value that gets us maxTorque
                        if ((aeroTorque + torqueDirection * torque).sqrMagnitude > maxTorque * maxTorque)
                        {
                            // Solution to the quadratic formula for the intersection of a line with a sphere, note we use the +ve solution
                            // There is no need to check the determinant as any line that originates within the sphere will always intersect the sphere
                            float temp = Vector3.Dot(aeroTorque, torqueDirection);
                            torque = BDAMath.Sqrt(temp * temp - (aeroTorque.sqrMagnitude - maxTorque * maxTorque)) - temp;
                            //Debug.Log($"[BDArmory.MissileGuidance]: torque saturation! torque = {torque}.");
                        }
                        // Otherwise we just use torque unmodified
                    }
                    else
                    {
                        //Debug.Log($"[BDArmory.MissileGuidance]: aeroTorque saturated! torque = {torque}.");
                        // If we're saturated, then as long as torqueDirection somewhat opposes aeroTorque we can look
                        // at how much torque we can apply
                        float temp = Vector3.Dot(aeroTorque, torqueDirection);
                        // We check the determinant of the quadratic as well to ensure we actually intersect with the sphere
                        float det = temp * temp - (aeroTorque.sqrMagnitude - maxTorque * maxTorque);
                        if (temp < 0 && det > 0)
                        {
                            float temp2 = BDAMath.Sqrt(det);
                            // temp2 > 0 and temp < 0 so LHS < RHS
                            float LHS = -temp2 - temp;
                            float RHS = temp2 - temp;
                            // There are three cases here, first is the case is if torque is insufficient to drive us under saturation,
                            // in which case we'll just apply enough to saturate
                            if (torque < LHS)
                                torque = LHS;
                            // The second case is where we've gone over in the opposite direction, in which case we must reduce our torque
                            else if (torque > RHS)
                                torque = RHS;
                            // A special case occurs if |temp| < Mathf.Epsilon, which is the single point intersection solution, where
                            // torque can potentially approx. equal the single point solution but in that case we wouldn't have to modify
                            // the torque. If torque is not approx. equal one of these two cases should catch it
                            // The third case is where we're perfectly within bounds so we don't modify torque
                            //Debug.Log($"[BDArmory.MissileGuidance]: Possible to unsaturate! torque = {torque}.");
                        }
                        else
                        {
                            // If all previous checks fail, we're saturated, so we limit the aeroTorque
                            torque = 0f;
                            aeroTorque = (maxTorque / aeroTorque.magnitude) * aeroTorque;
                            //Debug.Log($"[BDArmory.MissileGuidance]: Cannot unsaturate! aeroTorque = {aeroTorque}.");
                        }
                    }

                    finalTorque = torque > 0f ? (torque * torqueDirection + aeroTorque) : aeroTorque;
                }
                else
                {
                    if (aeroTorque.sqrMagnitude > maxTorque * maxTorque)
                    {
                        aeroTorque = (maxTorque / aeroTorque.magnitude) * aeroTorque;
                    }

                    finalTorque = aeroTorque;
                }
                
                finalTorque = ml.transform.InverseTransformDirection(finalTorque).ProjectOnPlanePreNormalized(Vector3.forward);

                //Debug.Log($"[BDArmory.MissileGuidance]: torque = {torque}, torqueDirection = {torqueDirection}, aeroTorque = {aeroTorque}, finalTorque = {finalTorque}.");

                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
            else
            {
                Vector3 finalTorque = Vector3.Lerp(previousTorque, Vector3.zero, 0.25f).ProjectOnPlanePreNormalized(Vector3.forward);
                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
        }

        public static float GetRadarAltitude(Vessel vessel)
        {
            float radarAlt = Mathf.Clamp((float)(vessel.mainBody.GetAltitude(vessel.CoM) - vessel.terrainAltitude), 0,
                (float)vessel.altitude);
            return radarAlt;
        }

        public static float GetRadarAltitudeAtPos(Vector3 position)
        {
            double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(position);
            double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(position);

            float radarAlt = Mathf.Clamp(
                (float)(FlightGlobals.currentMainBody.GetAltitude(position) -
                         FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos)), 0,
                (float)FlightGlobals.currentMainBody.GetAltitude(position));
            return radarAlt;
        }

        public static float GetRaycastRadarAltitude(Vector3 position)
        {
            Vector3 upDirection = VectorUtils.GetUpDirection(position);

            float altAtPos = FlightGlobals.getAltitudeAtPos(position);
            if (altAtPos < 0)
            {
                position += 2 * Mathf.Abs(altAtPos) * upDirection;
            }

            Ray ray = new Ray(position, -upDirection);
            float rayDistance = FlightGlobals.getAltitudeAtPos(position);

            if (rayDistance < 0)
            {
                return 0;
            }

            RaycastHit rayHit;
            if (Physics.Raycast(ray, out rayHit, rayDistance, (int)(LayerMasks.Scenery | LayerMasks.EVA))) // Why EVA?
            {
                return rayHit.distance;
            }
            else
            {
                return rayDistance;
            }
        }
    }
}
