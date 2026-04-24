using BDArmory.Modules;
using BDArmory.Targeting;
using UnityEngine;

namespace BDArmory.Radar
{
    public struct RadarDisplayData
    {
        public Vessel vessel;
        public Vector2 pingPosition;
        public bool locked;
        public ModuleRadar detectedByRadar;
        public TargetSignatureData targetData;
        public float signalPersistTime;
        public float velAngle;
        public int jammedIndex;
    }
    public struct IRSTDisplayData
    {
        public Vessel vessel;
        public Vector2 pingPosition;
        public float magnitude;
        public ModuleIRST detectedByIRST;
        public TargetSignatureData targetData;
        public float signalPersistTime;
    }
}
