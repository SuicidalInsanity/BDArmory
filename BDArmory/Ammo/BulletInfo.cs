using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BDArmory.Bullets
{
    public class BulletInfo
    {
        public string name { get; private set; }
        public string DisplayName { get; private set; }
        public float caliber { get; private set; }
        public float bulletMass { get; private set; }
        public float bulletVelocity { get; private set; }
        public string explosive { get; private set; } //left for legacy support
        public bool incendiary { get; private set; } //left for legacy support
        //public string attributeTags { get; private set; } //replace this with a string? tags to add: HE, incendiary, EMP, nuclear, beehive, homing, massmod, impulse; 
                                                          //nuclear can use tntmass for kT, beehive can use submunition#, would need submunition bulletType, homing would need degrees/s, massmod needs mass mod, impulse needs impulse
        public bool EMP { get; private set; }
        public bool nuclear { get; private set; }
        public bool beehive { get; private set; }
        public string subMunitionType { get; private set; }
        public float tntMass { get; private set; }
        public float massMod { get; private set; }
        public float impulse { get; private set; }
        public string fuzeType { get; private set; }
        public int subProjectileCount { get; private set; }
        public float apBulletMod { get; private set; }
        public string bulletDragTypeName { get; private set; }
        public string projectileColor { get; private set; }
        public string startColor { get; private set; }
        public bool fadeColor { get; private set; }

        public static BulletInfos bullets;
        public static HashSet<string> bulletNames;
        public static BulletInfo defaultBullet;

        public BulletInfo(string name, string DisplayName, float caliber, float bulletVelocity, float bulletMass, 
                          string explosive, bool incendiary, float tntMass, bool EMP, bool nuclear, bool beehive, string subMunitionType, float massMod, float impulse, string fuzeType, float apBulletDmg,
                          int subProjectileCount, string bulletDragTypeName, string projectileColor, string startColor, bool fadeColor)
        {
            this.name = name;
            this.DisplayName = DisplayName;
            this.caliber = caliber;
            this.bulletVelocity = bulletVelocity;
            this.bulletMass = bulletMass;
            this.explosive = explosive;
            this.incendiary = incendiary;
            this.tntMass = tntMass;
            this.EMP = EMP;
            this.nuclear = nuclear;
            this.beehive = beehive;
            this.subMunitionType = subMunitionType;
            this.massMod = massMod;
            this.impulse = impulse;
            this.fuzeType = fuzeType;
            this.apBulletMod = apBulletDmg;
            this.subProjectileCount = subProjectileCount;
            this.bulletDragTypeName = bulletDragTypeName;
            this.projectileColor = projectileColor;
            this.startColor = startColor;
            this.fadeColor = fadeColor;
        }

        public static void Load()
        {
            if (bullets != null) return; // Only load the bullet defs once on startup.
            bullets = new BulletInfos();
            if (bulletNames == null) bulletNames = new HashSet<string>();
            UrlDir.UrlConfig[] nodes = GameDatabase.Instance.GetConfigs("BULLET");
            ConfigNode node;

            // First locate BDA's default bullet definition so we can fill in missing fields.
            if (defaultBullet == null)
                for (int i = 0; i < nodes.Length; ++i)
                {
                    if (nodes[i].parent.name != "BD_Bullets") continue; // Ignore other config files.
                    node = nodes[i].config;
                    if (!node.HasValue("name") || (string)ParseField(nodes[i].config, "name", typeof(string)) != "def") continue; // Ignore other configs.
                    Debug.Log("[BDArmory.BulletInfo]: Parsing default bullet definition from " + nodes[i].parent.name);
                    //tagsList = Misc.BDAcTools.ParseNames((string)ParseField(node, "attributeTags", typeof(string))); //would prefer not to do a rocketInfo and have separate node fields for every attribute
                    defaultBullet = new BulletInfo(
                        "def",
                        (string)ParseField(node, "DisplayName", typeof(string)),
                        (float)ParseField(node, "caliber", typeof(float)),
                        (float)ParseField(node, "bulletVelocity", typeof(float)),
                        (float)ParseField(node, "bulletMass", typeof(float)),
                        (string)ParseField(node, "explosive", typeof(string)),
                        (bool)ParseField(node, "incendiary", typeof(bool)),
                        (float)ParseField(node, "tntMass", typeof(float)),
                        (bool)ParseField(node, "EMP", typeof(bool)),
                        (bool)ParseField(node, "nuclear", typeof(bool)),
                        (bool)ParseField(node, "beehive", typeof(bool)),
                        (string)ParseField(node, "subMunitionType", typeof(string)),
                        (float)ParseField(node, "massMod", typeof(float)),
                        (float)ParseField(node, "impulse", typeof(float)),
                        (string)ParseField(node, "fuzeType", typeof(string)),
                        (float)ParseField(node, "apBulletMod", typeof(float)),
                        Math.Max((int)ParseField(node, "subProjectileCount", typeof(int)), 1),
                        (string)ParseField(node, "bulletDragTypeName", typeof(string)),
                        (string)ParseField(node, "projectileColor", typeof(string)),
                        (string)ParseField(node, "startColor", typeof(string)),
                        (bool)ParseField(node, "fadeColor", typeof(bool))
                    );
                    bullets.Add(defaultBullet);
                    bulletNames.Add("def");
                    break;
                }
            if (defaultBullet == null) throw new ArgumentException("Failed to find BDArmory's default bullet definition.", "defaultBullet");

            // Now add in the rest of the bullets.
            for (int i = 0; i < nodes.Length; i++)
            {
                string name_ = "";
                try
                {
                    node = nodes[i].config;
                    name_ = (string)ParseField(node, "name", typeof(string));
                    if (bulletNames.Contains(name_)) // Avoid duplicates.
                    {
                        if (nodes[i].parent.name != "BD_Bullets" || name_ != "def") // Don't report the default bullet definition as a duplicate.
                            Debug.LogError("[BDArmory.BulletInfo]: Bullet definition " + name_ + " from " + nodes[i].parent.name + " already exists, skipping.");
                        continue;
                    }
                    Debug.Log("[BDArmory.BulletInfo]: Parsing definition of bullet " + name_ + " from " + nodes[i].parent.name);
                    bullets.Add(
                        new BulletInfo(
                            name_,
                        (string)ParseField(node, "DisplayName", typeof(string)),
                        (float)ParseField(node, "caliber", typeof(float)),
                        (float)ParseField(node, "bulletVelocity", typeof(float)),
                        (float)ParseField(node, "bulletMass", typeof(float)),
                        (string)ParseField(node, "explosive", typeof(string)),
                        (bool)ParseField(node, "incendiary", typeof(bool)),
                        (float)ParseField(node, "tntMass", typeof(float)),
                        (bool)ParseField(node, "EMP", typeof(bool)),
                        (bool)ParseField(node, "nuclear", typeof(bool)),
                        (bool)ParseField(node, "beehive", typeof(bool)),
                        (string)ParseField(node, "subMunitionType", typeof(string)),
                        (float)ParseField(node, "massMod", typeof(float)),
                        (float)ParseField(node, "impulse", typeof(float)),
                        (string)ParseField(node, "fuzeType", typeof(string)),
                        (float)ParseField(node, "apBulletMod", typeof(float)),
                        (int)ParseField(node, "subProjectileCount", typeof(int)),
                        (string)ParseField(node, "bulletDragTypeName", typeof(string)),
                        (string)ParseField(node, "projectileColor", typeof(string)),
                        (string)ParseField(node, "startColor", typeof(string)),
                        (bool)ParseField(node, "fadeColor", typeof(bool))
                        )
                    );
                    bulletNames.Add(name_);
                }
                catch (Exception e)
                {
                    Debug.LogError("[BDArmory.BulletInfo]: Error Loading Bullet Config '" + name_ + "' | " + e.ToString());
                }
            }
        }
        private static object ParseField(ConfigNode node, string field, Type type)
        {
            try
            {
                if (!node.HasValue(field))
                {
                    throw new ArgumentNullException(field, "Field '" + field + "' is missing.");
                }
                var value = node.GetValue(field);
                try
                {
                    if (type == typeof(string))
                    { return value; }
                    else if (type == typeof(bool))
                    { return bool.Parse(value); }
                    else if (type == typeof(int))
                    { return int.Parse(value); }
                    else if (type == typeof(float))
                    { return float.Parse(value); }
                    else
                    { throw new ArgumentException("Invalid type specified."); }
                }
                catch (Exception e)
                { throw new ArgumentException("Field '" + field + "': '" + value + "' could not be parsed as '" + type.ToString() + "' | " + e.ToString(), field); }
            }
            catch (Exception e)
            {
                if (defaultBullet != null)
                {
                    // Give a warning about the missing or invalid value, then use the default value using reflection to find the field.
                    if (field == "DisplayName") return string.Empty;
                    var defaultValue = typeof(BulletInfo).GetProperty(field == "DisplayName" ? "name" : field, BindingFlags.Public | BindingFlags.Instance).GetValue(defaultBullet); //this is returnin the def bullet name, not current bullet name
                    if (field == "EMP" || field == "nuclear" || field == "beehive" || field == "subMunitionType" || field == "massMod" || field == "impulse")
                    {
                        //not having these throw an error message since these are all optional and default to false, prevents bullet defs from bloating like rockets did
                        //Future SI - apply this to rocket, mutator defs
                    }
                    else
                    {
                        Debug.LogError("[BDArmory.BulletInfo]: Using default value of " + defaultValue.ToString() + " for " + field + " | " + e.ToString());
                    }
                    return defaultValue;
                }
                else
                    throw;
            }
        }
    }

    public class BulletInfos : List<BulletInfo>
    {
        public BulletInfo this[string name]
        {
            get { return Find((value) => { return value.name == name; }); }
        }
    }
}
