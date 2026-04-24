namespace BDArmory.Weapons
{
    public interface IBDWeapon
    {
        WeaponClasses GetWeaponClass();

        string GetShortName();

        string GetSubLabel();

        float GetEngageRange();

        float GetEngageFOV();

        string GetMissileType();

        string GetPartName();

        float GetWeaponChannel();

        Part GetPart();

        ModuleWeapon GetWeaponModule();

        // extensions for feature_engagementenvelope
    }

    // extensions for feature_engagementenvelope

    public enum WeaponClasses
    {
        Missile,
        Bomb,
        Gun,
        Rocket,
        DefenseLaser,
        SLW
    }
}
