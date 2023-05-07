using System;
using UnityEngine;

namespace BDArmory.Utils
{
    public static class BodyUtils
    {
        public static string FormattedGeoPos(Vector3d geoPos, bool altitude)
        {
            string finalString = string.Empty;
            //lat
            double lat = geoPos.x;
            double latSign = Math.Sign(lat);
            double latMajor = latSign * Math.Floor(Math.Abs(lat));
            double latMinor = 100 * (Math.Abs(lat) - Math.Abs(latMajor));
            string latString = latMajor.ToString("0") + " " + latMinor.ToString("0.000");
            finalString += "N:" + latString;

            //longi
            double longi = geoPos.y;
            double longiSign = Math.Sign(longi);
            double longiMajor = longiSign * Math.Floor(Math.Abs(longi));
            double longiMinor = 100 * (Math.Abs(longi) - Math.Abs(longiMajor));
            string longiString = longiMajor.ToString("0") + " " + longiMinor.ToString("0.000");
            finalString += " E:" + longiString;

            if (altitude)
            {
                finalString += " ASL:" + geoPos.z.ToString("0.000");
            }

            return finalString;
        }

        public static string FormattedGeoPosShort(Vector3d geoPos, bool altitude)
        {
            string finalString = string.Empty;
            //lat
            double lat = geoPos.x;
            double latSign = Math.Sign(lat);
            double latMajor = latSign * Math.Floor(Math.Abs(lat));
            double latMinor = 100 * (Math.Abs(lat) - Math.Abs(latMajor));
            string latString = latMajor.ToString("0") + " " + latMinor.ToString("0");
            finalString += "N:" + latString;

            //longi
            double longi = geoPos.y;
            double longiSign = Math.Sign(longi);
            double longiMajor = longiSign * Math.Floor(Math.Abs(longi));
            double longiMinor = 100 * (Math.Abs(longi) - Math.Abs(longiMajor));
            string longiString = longiMajor.ToString("0") + " " + longiMinor.ToString("0");
            finalString += " E:" + longiString;

            if (altitude)
            {
                finalString += " ASL:" + geoPos.z.ToString("0");
            }

            return finalString;
        }

        public static float GetRadarAltitudeAtPos(Vector3 position, bool clamped = true)
        {
            double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(position);
            double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(position);
            float altitude = (float)(FlightGlobals.currentMainBody.GetAltitude(position));
            if (clamped)
                return Mathf.Clamp(altitude - (float)FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos), 0, altitude);
            else
                return altitude - (float)FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos);
        }

        public static double GetTerrainAltitudeAtPos(Vector3 position, bool allowNegative = false)
        {
            double latitudeAtPos = FlightGlobals.currentMainBody.GetLatitude(position);
            double longitudeAtPos = FlightGlobals.currentMainBody.GetLongitude(position);
            return FlightGlobals.currentMainBody.TerrainAltitude(latitudeAtPos, longitudeAtPos, allowNegative);
        }
    }
}