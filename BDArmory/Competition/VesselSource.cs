using System;
namespace BDArmory.Competition
{
    public interface VesselSource
    {
        VesselModel GetVessel(int id);
        string GetLocalPath(int id);
    }
}
