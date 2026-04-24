using System;
using UnityEngine;

using BDArmory.Extensions;
using System.Runtime.CompilerServices;

namespace BDArmory.Utils
{
    public static class VectorUtils
    {
        private static System.Random RandomGen = new System.Random();

        /// <summary>
        /// A slightly more efficient `Vector3.Sign` function, still requires a sqrt so it is best replaced with
        /// `VectorUtils.GetAngleOnPlane`, however that requires orthogonality from `fromDirection`. This function
        /// may be used even if `referenceRight` is not orthogonal to `fromDirection`. This function also does not
        /// require the magnitudes of any of its inputs to be specified in some way.
        /// </summary>
        /// <param name="referenceRight">Right compared to fromDirection, make sure it's not orthogonal to toDirection, or you'll get unstable signs</param>
        public static float SignedAngle(Vector3 fromDirection, Vector3 toDirection, Vector3 referenceRight)
        {
            float angle = Angle(fromDirection, toDirection);
            float sign = Mathf.Sign(Vector3.Dot(toDirection, referenceRight));
            float finalAngle = sign * angle;
            return finalAngle;
        }

        /// <summary>
        /// Same as SignedAngle, just using double precision for the cosine calculation.
        /// For very small angles the floating point precision starts to matter, as the cosine is close to 1, not to 0.
        /// </summary>
        public static float SignedAngleDP(Vector3 fromDirection, Vector3 toDirection, Vector3 referenceRight)
        {
            float angle = (float)Vector3d.Angle(fromDirection, toDirection);
            float sign = Mathf.Sign(Vector3.Dot(toDirection, referenceRight));
            float finalAngle = sign * angle;
            return finalAngle;
        }

        /// <summary>
        /// Convert an angle to be between -180 and 180.
        /// </summary>
        public static float ToAngle(this float angle)
        {
            angle = (angle + 180) % 360;
            return angle > 0 ? angle - 180 : angle + 180;
        }

        //from howlingmoonsoftware.com
        //calculates how long it will take for a target to be where it will be when a bullet fired now can reach it.
        //delta = initial relative position, vr = relative velocity, muzzleV = bullet velocity.
        public static float CalculateLeadTime(Vector3 delta, Vector3 vr, float muzzleV)
        {
            // Quadratic equation coefficients a*t^2 + b*t + c = 0
            float a = Vector3.Dot(vr, vr) - muzzleV * muzzleV;
            float b = 2f * Vector3.Dot(vr, delta);
            float c = Vector3.Dot(delta, delta);

            float det = b * b - 4f * a * c;

            // If the determinant is negative, then there is no solution
            if (det > 0f)
            {
                return 2f * c / (BDAMath.Sqrt(det) - b);
            }
            else
            {
                return -1f;
            }
        }

        /// <summary>
        /// Returns a value between -1 and 1 via Perlin noise.
        /// </summary>
        /// <returns>Returns a value between -1 and 1 via Perlin noise.</returns>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        public static float FullRangePerlinNoise(float x, float y)
        {
            float perlin = Mathf.PerlinNoise(x, y);

            perlin -= 0.5f;
            perlin *= 2;

            return perlin;
        }

        public static Vector3 RandomDirectionDeviation(Vector3 direction, float maxAngle)
        {
            return Vector3.RotateTowards(direction, UnityEngine.Random.rotation * direction, UnityEngine.Random.Range(0, maxAngle * Mathf.Deg2Rad), 0).normalized;
        }

        public static Vector3 WeightedDirectionDeviation(Vector3 direction, float maxAngle)
        {
            float random = UnityEngine.Random.Range(0f, 1f);
            float maxRotate = maxAngle * (random * random);
            maxRotate = Mathf.Clamp(maxRotate, 0, maxAngle) * Mathf.Deg2Rad;
            return Vector3.RotateTowards(direction, UnityEngine.Random.onUnitSphere.ProjectOnPlane(direction), maxRotate, 0).normalized;
        }

        /// <summary>
        /// Returns the original vector rotated in a random direction using the give standard deviation.
        /// </summary>
        /// <param name="direction">mean direction</param>
        /// <param name="standardDeviation">standard deviation in degrees</param>
        /// <returns>Randomly adjusted Vector3</returns>
        /// <remarks>
        /// Technically, this is calculated using the chi-squared distribution in polar coordinates,
        /// which, incidentally, makes the math easier too.
        /// However a chi-squared (k=2) distance from center distribution produces a vector distributed normally
        /// on any chosen axis orthogonal to the original vector, which is exactly what we want.
        /// </remarks>
        public static Vector3 GaussianDirectionDeviation(Vector3 direction, float standardDeviation)
        {
            return Quaternion.AngleAxis(UnityEngine.Random.Range(-180f, 180f), direction)
                * Quaternion.AngleAxis(Rayleigh() * standardDeviation,
                                       new Vector3(-1 / direction.x, -1 / direction.y, 2 / direction.z))  // orthogonal vector
                * direction;
        }

        /// <returns>Random float distributed with an approximated standard normal distribution</returns>
        /// <see>https://en.wikipedia.org/wiki/Box%E2%80%93Muller_transform</see>
        /// <remarks>Note a standard normal variable is technically unbounded</remarks>
        public static float Gaussian()
        {
            // Technically this will raise an exception if the first random produces a zero (which should never happen now that it's log(1-rnd))
            try
            {
                return BDAMath.Sqrt(-2 * Mathf.Log(1f - UnityEngine.Random.value)) * Mathf.Cos(Mathf.PI * UnityEngine.Random.value);
            }
            catch (Exception e)
            { // I have no idea what exception Mathf.Log raises when it gets a zero
                Debug.LogWarning("[BDArmory.VectorUtils]: Exception thrown in Gaussian: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
        }

        /// <summary>
        /// Generate a Vector3 with elements from an approximately normal distribution (mean: 0, std.dev: 1).
        /// </summary>
        /// <returns></returns>
        public static Vector3 GaussianVector3()
        {
            return new Vector3(Gaussian(), Gaussian(), Gaussian());
        }

        public static Vector3d GaussianVector3d(Vector3d mean, Vector3d stdDev)
        {
            return new Vector3d(
                mean.x + stdDev.x * Math.Sqrt(-2 * Math.Log(1 - RandomGen.NextDouble())) * Math.Cos(Math.PI * RandomGen.NextDouble()),
                mean.y + stdDev.y * Math.Sqrt(-2 * Math.Log(1 - RandomGen.NextDouble())) * Math.Cos(Math.PI * RandomGen.NextDouble()),
                mean.z + stdDev.z * Math.Sqrt(-2 * Math.Log(1 - RandomGen.NextDouble())) * Math.Cos(Math.PI * RandomGen.NextDouble())
            );
        }

        /// <returns>
        /// Random float distributed with the chi-squared distribution with two degrees of freedom
        /// aka the Rayleigh distribution.
        /// Multiply by deviation for best results.
        /// </returns>
        /// <see>https://en.wikipedia.org/wiki/Rayleigh_distribution</see>
        /// <remarks>Note a chi-square distributed variable is technically unbounded</remarks>
        public static float Rayleigh()
        {
            // Technically this will raise an exception if the random produces a zero, which should almost never happen
            try
            {
                return BDAMath.Sqrt(-2 * Mathf.Log(UnityEngine.Random.value));
            }
            catch (Exception e)
            { // I have no idea what exception Mathf.Log raises when it gets a zero
                Debug.LogWarning("[BDArmory.VectorUtils]: Exception thrown in Rayleigh: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
        }

        /// <summary>
        /// Converts world position to Lat,Long,Alt form.
        /// </summary>
        /// <returns>The position in geo coords.</returns>
        /// <param name="worldPosition">World position.</param>
        /// <param name="body">Body.</param>
        public static Vector3d WorldPositionToGeoCoords(Vector3d worldPosition, CelestialBody body)
        {
            if (!body)
            {
                return Vector3d.zero;
            }

            double lat = body.GetLatitude(worldPosition);
            double longi = body.GetLongitude(worldPosition);
            double alt = body.GetAltitude(worldPosition);
            return new Vector3d(lat, longi, alt);
        }

        /// <summary>
        /// Calculates the coordinates of a point a certain distance away in a specified direction.
        /// </summary>
        /// <param name="start">Starting point coordinates, in Lat,Long,Alt form</param>
        /// <param name="body">The body on which the movement is happening</param>
        /// <param name="bearing">Bearing to move in, in degrees, where 0 is north and 90 is east</param>
        /// <param name="distance">Distance to move, in meters</param>
        /// <returns>Ending point coordinates, in Lat,Long,Alt form</returns>
        public static Vector3 GeoCoordinateOffset(Vector3 start, CelestialBody body, float bearing, float distance)
        {
            //https://stackoverflow.com/questions/2637023/how-to-calculate-the-latlng-of-a-point-a-certain-distance-away-from-another
            float lat1 = start.x * Mathf.Deg2Rad;
            float lon1 = start.y * Mathf.Deg2Rad;
            bearing *= Mathf.Deg2Rad;
            distance /= ((float)body.Radius + start.z);

            float lat2 = Mathf.Asin(Mathf.Sin(lat1) * Mathf.Cos(distance) + Mathf.Cos(lat1) * Mathf.Sin(distance) * Mathf.Cos(bearing));
            float lon2 = lon1 + Mathf.Atan2(Mathf.Sin(bearing) * Mathf.Sin(distance) * Mathf.Cos(lat1), Mathf.Cos(distance) - Mathf.Sin(lat1) * Mathf.Sin(lat2));

            return new Vector3(lat2 * Mathf.Rad2Deg, lon2 * Mathf.Rad2Deg, start.z);
        }

        /// <summary>
        /// Calculate the bearing going from one point to another
        /// </summary>
        /// <param name="start">Starting point coordinates, in Lat,Long,Alt form</param>
        /// <param name="destination">Destination point coordinates, in Lat,Long,Alt form</param>
        /// <returns>Bearing when looking at destination from start, in degrees, where 0 is north and 90 is east</returns>
        public static float GeoForwardAzimuth(Vector3 start, Vector3 destination)
        {
            //http://www.movable-type.co.uk/scripts/latlong.html
            float lat1 = start.x * Mathf.Deg2Rad;
            float lon1 = start.y * Mathf.Deg2Rad;
            float lat2 = destination.x * Mathf.Deg2Rad;
            float lon2 = destination.y * Mathf.Deg2Rad;
            return Mathf.Atan2(Mathf.Sin(lon2 - lon1) * Mathf.Cos(lat2), Mathf.Cos(lat1) * Mathf.Sin(lat2) - Mathf.Sin(lat1) * Mathf.Cos(lat2) * Mathf.Cos(lon2 - lon1)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Calculate the distance from one point to another on a globe
        /// </summary>
        /// <param name="start">Starting point coordinates, in Lat,Long,Alt form</param>
        /// <param name="destination">Destination point coordinates, in Lat,Long,Alt form</param>
        /// <param name="body">The body on which the distance is calculated</param>
        /// <returns>distance between the two points</returns>
        public static float GeoDistance(Vector3 start, Vector3 destination, CelestialBody body)
        {
            //http://www.movable-type.co.uk/scripts/latlong.html
            float lat1 = start.x * Mathf.Deg2Rad;
            float lat2 = destination.x * Mathf.Deg2Rad;
            float dlat = lat2 - lat1;
            float dlon = (destination.y - start.y) * Mathf.Deg2Rad;
            float a = Mathf.Sin(dlat / 2) * Mathf.Sin(dlat / 2) + Mathf.Cos(lat1) * Mathf.Cos(lat2) * Mathf.Sin(dlon / 2) * Mathf.Sin(dlon / 2);
            float distance = 2 * Mathf.Atan2(BDAMath.Sqrt(a), BDAMath.Sqrt(1 - a)) * (float)body.Radius;
            return BDAMath.Sqrt(distance * distance + (destination.z - start.z) * (destination.z - start.z));
        }

        public static Vector3 RotatePointAround(Vector3 pointToRotate, Vector3 pivotPoint, Vector3 axis, float angle)
        {
            Vector3 line = pointToRotate - pivotPoint;
            line = Quaternion.AngleAxis(angle, axis) * line;
            return pivotPoint + line;
        }

        public static Vector3 GetNorthVector(Vector3 position, CelestialBody body)
        {
            var latlon = body.GetLatitudeAndLongitude(position);
            var surfacePoint = body.GetWorldSurfacePosition(latlon.x, latlon.y, 0);
            var up = (body.GetWorldSurfacePosition(latlon.x, latlon.y, 1000) - surfacePoint).normalized;
            var north = -Math.Sign(latlon.x) * (body.GetWorldSurfacePosition(latlon.x - Math.Sign(latlon.x), latlon.y, 0) - surfacePoint).ProjectOnPlanePreNormalized(up).normalized;
            return north;
        }

        /// <summary>
        /// Efficiently calculate up, north and right at a given worldspace position on a body.
        /// </summary>
        /// <param name="body"></param>
        /// <param name="position"></param>
        /// <param name="up"></param>
        /// <param name="north"></param>
        /// <param name="right"></param>
        public static void GetWorldCoordinateFrame(CelestialBody body, Vector3 position, out Vector3 up, out Vector3 north, out Vector3 right)
        {
            var latlon = body.GetLatitudeAndLongitude(position);
            var surfacePoint = body.GetWorldSurfacePosition(latlon.x, latlon.y, 0);
            up = (body.GetWorldSurfacePosition(latlon.x, latlon.y, 1000) - surfacePoint).normalized;
            north = -Math.Sign(latlon.x) * (body.GetWorldSurfacePosition(latlon.x - Math.Sign(latlon.x), latlon.y, 0) - surfacePoint).ProjectOnPlanePreNormalized(up).normalized;
            right = Vector3.Cross(up, north);
        }

        public static Vector3 GetWorldSurfacePostion(Vector3d geoPosition, CelestialBody body)
        {
            if (!body)
            {
                return Vector3.zero;
            }
            return body.GetWorldSurfacePosition(geoPosition.x, geoPosition.y, geoPosition.z);
        }

        /// <summary>
        /// Get the up direction at a position.
        /// Note: If the position is a vessel's position, then this is the same as vessel.up, which is precomputed. Use that instead!
        /// </summary>
        /// <param name="position"></param>
        /// <returns>The normalized up direction at the position.</returns>
        public static Vector3 GetUpDirection(Vector3 position)
        {
            if (FlightGlobals.currentMainBody == null) return Vector3.up;
            return (position - FlightGlobals.currentMainBody.position).normalized;
        }

        /// <summary>
        /// Get the up direction and altitude at a position.
        /// Note: If the position is a vessel's position, then this is the same as vessel.up and vessel.altitude, which are precomputed. Use those instead!
        /// </summary>
        /// <param name="position"></param>
        /// <param name="altitude"></param>
        /// <returns>The normalized up direction at the position.</returns>
        public static Vector3 GetUpDirection(Vector3 position, out double altitude)
        {
            if (FlightGlobals.currentMainBody == null)
            {
                altitude = 0;
                return Vector3.up;
            }
            Vector3 upDir;
            (altitude, upDir) = (position - FlightGlobals.currentMainBody.position).MagNorm();
            altitude -= FlightGlobals.currentMainBody.Radius;

            return upDir;
        }

        public static bool SphereRayIntersect(Ray ray, Vector3 sphereCenter, double sphereRadius, out double distance)
        {
            Vector3 o = ray.origin;
            Vector3 l = ray.direction;
            Vector3d c = sphereCenter;
            double r = sphereRadius;

            double d;

            var dotLOC = Vector3.Dot(l, o - c);
            d = -(Vector3.Dot(l, o - c) + Math.Sqrt(dotLOC * dotLOC - (o - c).sqrMagnitude + (r * r)));

            if (double.IsNaN(d))
            {
                distance = 0;
                return false;
            }
            else
            {
                distance = d;
                return true;
            }
        }

        public static bool CheckClearOfSphere(Ray ray, Vector3 sphereCenter, float sphereRadius)
        {
            // Return true if no sphere intersections, false if sphere intersections
            // Better handling of conditions when ray origin is inside sphere or direction is away from sphere than SphereRayIntersect

            if ((ray.origin - sphereCenter).sqrMagnitude < (sphereRadius * sphereRadius))
                return false;

            bool intersect = SphereRayIntersect(ray, sphereCenter, (double)sphereRadius, out double distance);

            if (!intersect)
                return true;
            else
            {
                if (distance > 0) // Valid intersection
                    return false;
                else // -ray intersects, but +ray does not
                    return true;
            }
        }

        /// <summary>
        /// A more accurate Angle that is maintains precision down to an angle of 1e-5
        /// (as compared to (float)Vector3d.Angle) instead of the 1e-2 that Vector3.Angle gives.
        /// Additionally, it's around 30% faster than Vector3.Angle and 12% faster than (float)Vector3d(from, to).
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(Vector3 from, Vector3 to)
        {
            double num = ((Vector3d)from).sqrMagnitude * ((Vector3d)to).sqrMagnitude;
            if (num < 1e-30)
            {
                return 0f;
            }

            double num2 = BDAMath.Clamp(Vector3d.Dot(from, to) / Math.Sqrt(num), -1.0, 1.0);
            return (float)(Math.Acos(num2) * 57.295779513082325);
        }

        /// <summary>
        /// Get angle between two pre-normalized vectors.
        /// 
        /// This implementation assumes that the input vectors are already normalized,
        /// skipping such checks and normalization that Vector3.Angle does.
        /// IMPORTANT NOTE: Unlike Vector3.Angle(), this returns 90° if one or both
        /// vectors are zero vectors! Vector3.Angle() returns 0° instead.
        /// If this behavior is undesireable, the "AnglePreNormalized" function which takes
        /// in the two original vectors and their magnitudes should be used instead.
        /// </summary>
        /// <param name="from">First vector.</param>
        /// <param name="to">Second vector.</param>
        /// <returns>The angle between the two vectors.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AnglePreNormalized(Vector3 from, Vector3 to)
        {
            float num2 = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
            return Mathf.Acos(num2) * 57.29578f;
        }

        /// <summary>
        /// Get angle between two vectors, with known magnitudes.
        /// 
        /// This implementation assumes that the magnitude of the input vectors is known,
        /// skipping some checks and normalization that Vector3.Angle does. It is not
        /// truly more efficient, however it is slightly more efficient when both
        /// magnitudes are already known.
        /// </summary>
        /// <param name="from">First vector.</param>
        /// <param name="to">Second vector.</param>
        /// <param name="fromMag">First vector magnitude.</param>
        /// <param name="toMag">Second vector magnitude.</param>
        /// <returns>The angle between the two vectors.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AnglePreNormalized(Vector3 from, Vector3 to, float fromMag, float toMag)
        {
            float num = fromMag * toMag;
            if (num < 1E-15f)
                return 0f;

            float num2 = Mathf.Clamp(Vector3.Dot(from, to) / (fromMag * toMag), -1f, 1f);
            return Mathf.Acos(num2) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Get AoA and Sideslip of a vector, relative to axes defined by forward and up.
        /// Note that forward and up are expected to be unit vectors, however dir does not have
        /// to be a unit vector!
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="forward">Aircraft aligned forward vector.</param>
        /// <param name="up">Aircraft aligned up/lift vector.</param>
        /// <param name="AoA">AoA output.</param>
        /// <param name="sideslip">Sideslip output.</param>
        /// <returns>The AoA and Sideslip angle, in degrees, of "dir" relative to the axes defined by forward and up.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetAoASideslip(Vector3 dir, Vector3 forward, Vector3 up, out float AoA, out float sideslip)
        {
            // Get the left vector to fully define the coordinate system
            Vector3 left = Vector3.Cross(up, forward);

            // Get the projections
            float x = Vector3.Dot(dir, forward);
            float y = Vector3.Dot(dir, left);
            float z = Vector3.Dot(dir, up);

            // Return the AoA/sideslip
            AoA = -Mathf.Rad2Deg * Mathf.Atan2(z, x);
            sideslip = -Mathf.Rad2Deg * Mathf.Atan2(y, x);
        }

        /// <summary>
        /// Get angle of a vector, projected on a plane defined by a forward and a left vector.
        /// Note that forward and left must have equal magnitudes but do not have to be unit
        /// vectors (though unit vectors are most likely the most convenient for this purpose).
        /// dir does not have to be a unit vector.
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="forward">Forward vector.</param>
        /// <param name="left">Left vector.</param>
        /// <returns>The angle of "dir" relative to "forward", in degrees, projected onto a plane defined by "forward" and "left".</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetAngleOnPlane(Vector3 dir, Vector3 forward, Vector3 left)
        {
            // Get the projections
            float x = Vector3.Dot(dir, forward);
            float y = Vector3.Dot(dir, left);

            // Check for if the desired vector is straight up/down
            if (Mathf.Abs(x) < 2f * Vector3.kEpsilon && Mathf.Abs(y) < 2f * Vector3.kEpsilon)
                return 0f;

            // Return the azimuth/elevation
            return Mathf.Rad2Deg * Mathf.Atan2(y, x);
        }

        /// <summary>
        /// Get elevation angle of a vector, relative to an up vector.
        /// Note that this basically an alternate form of AnglePreNormalized.
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="up">Up vector.</param>
        /// <param name="dist">Magnitude of the direction vector.</param>
        /// <param name="upMag">Magnitude of the up vector, defaults to 1.</param>
        /// <returns>The angle of "dir" relative to "up", in degrees, as an elevation angle, with range -90° to 90°.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElevation(Vector3 dir, Vector3 up, float dist, float upMag = 1.0f)
        {
            return 90f - AnglePreNormalized(up, dir, upMag, dist);
        }

        /// <summary>
        /// Get elevation angle of a vector, relative to an up vector.
        /// Note that this basically an alternate form of Vector3.Angle,
        /// somewhat optimized for the case where the up vector is a
        /// unit vector (skipping a mere "sqrMagnitude" call). If the
        /// magnitude of the direction vector is known, the overload
        /// with this magnitude is preferred:
        /// GetElevation(dir, up, dist, upMag)
        /// 
        /// </summary>
        /// <param name="dir">Direction vector.</param>
        /// <param name="up">Up vector.</param>
        /// <returns>The angle of "dir" relative to "up", in degrees, as an elevation angle, with range -90° to 90°.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElevation(Vector3 dir, Vector3 up)
        {
            float dirMag = dir.magnitude;
            if (dirMag < 1E-15f)
            {
                return 0f;
            }

            float num2 = Mathf.Clamp(Vector3.Dot(up, dir) / dirMag, -1f, 1f);
            return 90f - (float)Math.Acos(num2) * 57.29578f;
        }

        /// <summary>
        /// Get normalized difference between two vectors, useful for direction vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>(v1 - v2).normalized.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 NormalizedDiff(Vector3 v1, Vector3 v2)
        {
            float x = v1.x - v2.x, y = v1.y - v2.y, z = v1.z - v2.z;
            float normalizationFactor = 1f / BDAMath.Sqrt(x * x + y * y + z * z);
            return new Vector3(x * normalizationFactor, y * normalizationFactor, z * normalizationFactor);
        }

        /// <summary>
        /// Get normalized difference between two vectors with given distance, useful for direction vectors.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <param name="dist">Distance.</param>
        /// <returns>(v1 - v2).normalized.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 NormalizedDiff(Vector3 v1, Vector3 v2, float dist)
        {
            float x = v1.x - v2.x, y = v1.y - v2.y, z = v1.z - v2.z;
            float normalizationFactor = 1f / dist;
            return new Vector3(x * normalizationFactor, y * normalizationFactor, z * normalizationFactor);
        }

        /// <summary>
        /// Get square distance between two vectors, in cases where the vector difference isn't needed.
        /// </summary>
        /// <param name="v1">First vector.</param>
        /// <param name="v2">Second vector.</param>
        /// <returns>(v1 - v2).sqrMagnitude.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrDist(Vector3 v1, Vector3 v2)
        {
            float x = v1.x - v2.x, y = v1.y - v2.y, z = v1.z - v2.z;
            return x * x + y * y + z * z;
        }

        /// <summary>
        /// Rotates a Vector2 in 2D about (0,0).
        /// </summary>
        /// <param name="v">Vector.</param>
        /// <param name="theta">Angle.</param>
        /// <returns>v rotated by theta degrees (anti-clockwise positive).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Rotate2DVec2(Vector2 v, float theta)
        {
            float x = v.x, y = v.y;
            float cos = Mathf.Cos(theta * Mathf.Deg2Rad);
            float sin = BDAMath.Sqrt(1 - cos * cos);
            return new Vector2(x * cos - y * sin, x * sin + y * cos);
        }

        /// <summary>
        /// Rotates a Vector2 in 2D about a given point.
        /// </summary>
        /// <param name="v">Vector to rotate.</param>
        /// <param name="p">Point to rotate about.</param>
        /// <param name="theta">Angle.</param>
        /// <returns>v rotated by theta degrees (anti-clockwise positive) about p.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Rotate2DVec2(Vector2 v, Vector2 p, float theta)
        {
            float x = v.x - p.x, y = v.y - p.y;
            float cos = Mathf.Cos(theta);
            float sin = BDAMath.Sqrt(1 - cos * cos);
            return new Vector2(x * cos - y * sin + p.x, x * sin + y * cos + p.y);
        }

        /// <summary>
        /// Compute the 1-norm of a Vector3.
        /// </summary>
        /// <returns>The 1-norm.</returns>
        public static float OneNorm(this Vector3 v)
        {
            return Mathf.Abs(v.x) + Mathf.Abs(v.y) + Mathf.Abs(v.z);
        }

        /// <summary>
        /// Round the Vector3 to the given unit.
        /// </summary>
        /// <param name="unit">The unit to round to.</param>
        /// <returns>The modified Vector3.</returns>
        public static Vector3 Round(this ref Vector3 v, float unit)
        {
            if (unit == 0) return v;
            v.x = Mathf.Round(v.x / unit) * unit;
            v.y = Mathf.Round(v.y / unit) * unit;
            v.z = Mathf.Round(v.z / unit) * unit;
            return v;
        }

        /// <summary>
        /// Non-modifying version of Vector3.Round.
        /// </summary>
        /// <param name="unit">The unit to round to.</param>
        /// <returns>A new Vector3 rounded to the unit.</returns>
        public static Vector3 Rounded(this Vector3 v, float unit) => v.Round(unit);
    }
}
