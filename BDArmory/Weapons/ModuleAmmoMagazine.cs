using BDArmory.Bullets;
using BDArmory.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace BDArmory.Weapons.Missiles
{
    public class ModuleAmmoMagazine : PartModule//, IPartMassModifier, IPartCostModifier
    {
        //public float GetModuleMass(float baseMass, ModifierStagingSituation situation) => Mathf.Max(ammoCapacity, 0) //need to have this scale by some amount

        //public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        //public float GetModuleCost(float baseCost, ModifierStagingSituation situation) => Mathf.Max(ammoCapacity, 0); //ditto. default BDA ammobox cost?
        //public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_AmmoCapacity"),//Ammo Capacity
UI_FloatSemiLogRange(minValue = 1f, maxValue = 4, stepIncrement = 1f, sigFig = 2, withZero = true, scene = UI_Scene.All)]
        public float ammoCapacity = 500;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorWidth"),// Length
UI_FloatRange(minValue = 1f, maxValue = 40, stepIncrement = 1f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float rowCount = 20;

        [KSPField(advancedTweakable = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_AmmoMass")]//Ammo Mass when full
        public string ammoMass = "0kg";

        [KSPField] 
        public float maxAmmo = 500;
        [KSPField] 
        public string ammoName = "50CalAmmo";
        [KSPField] 
        public float cartridgeDiameter = 12.7f; //in millimeters

        [KSPField]
        public float roundLength = -1; //length of entire round, in mm
        [KSPField(isPersistant = true)]
        public Vector2 bulletScale = Vector2.zero;
        [KSPField]
        public bool isRectangularMagazine = true;

        [KSPField]
        public string scaleTransformName;
        Transform ScaleTransform;

        PartResource ammoResource;

        [KSPField] public string stackNodePosition;

        Dictionary<string, Vector3> originalStackNodePosition;

        public void Start()
        {
            var caliber = cartridgeDiameter / 1000;
            
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (bulletScale == Vector2.zero)
                {
                    if (!isRectangularMagazine)
                    {
                        //assume drum dia is 3x bullet length
                        //cartridge length = 6-7 caliber? breaks down for low vel stuff like pistol/grenade rounds
                        //exposed bullet length = 2-3 caliber, depending on type
                        //gets you bulletlength = 9 x caliber; drum diameter = 27 * caliber (plus 1 for material of drum wall thickness)
                        //at a drum dia of 3x bullet length you can get 36 per one layer
                        //length is (ammoAmount / 36), rounded up, + 1 beause it's a spiral, not flat, caliber
                        var length = roundLength > 0 ? (roundLength * 3) / 1000 : caliber * 27;
                        bulletScale = new Vector2(length, caliber); //drum diameter, width
                    }
                    else
                    {
                        var length = roundLength > 0 ? roundLength / 1000 : caliber * 9;
                        if (bulletScale == Vector2.zero) bulletScale = new Vector2(caliber, roundLength); //width, length
                    }
                }
                if (!isRectangularMagazine)
                {
                    Fields[nameof(rowCount)].guiActiveEditor = false;
                }
                ParseStackNodePosition();
            }
            using (IEnumerator<PartResource> res = part.Resources.GetEnumerator())
                while (res.MoveNext())
                {
                    if (res.Current == null) continue;
                    if (res.Current.resourceName == ammoName) ammoResource = res.Current;
                    break;
                }
            StartCoroutine(DelayedStart());
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitForFixedUpdate();

            if (string.IsNullOrEmpty(scaleTransformName) || (ScaleTransform = part.FindModelTransform(scaleTransformName)) == null)
            {
                Fields[nameof(ammoCapacity)].guiActiveEditor = false;
                Fields[nameof(rowCount)].guiActiveEditor = false;
            }
            else
            {
                UI_FloatSemiLogRange scale = (UI_FloatSemiLogRange)Fields[nameof(ammoCapacity)].uiControlEditor;
                scale.UpdateLimits(scale.minValue, maxAmmo);
                scale.maxValue = maxAmmo;
                scale.onFieldChanged = UpdateScale;
                UI_FloatRange rows = (UI_FloatRange)Fields[nameof(rowCount)].uiControlEditor;
                rows.maxValue = Mathf.CeilToInt(BDAMath.Sqrt(maxAmmo));
                rows.onFieldChanged = UpdateScale;
            }
            UpdateScaling(bulletScale);
        }

        void ParseStackNodePosition()
        {
            originalStackNodePosition = new Dictionary<string, Vector3>();
            string[] nodes = stackNodePosition.Split(new char[] { ';' });
            for (int i = 0; i < nodes.Length; i++)
            {
                string[] split = nodes[i].Split(new char[] { ',' });
                string id = split[0];
                Vector3 position = new Vector3(float.Parse(split[1]), float.Parse(split[2]), float.Parse(split[3]));
                originalStackNodePosition.Add(id, position);
            }
        }

        public void UpdateScale(BaseField field, object obj)
        {
            if (ScaleTransform != null)
            {
                if (isRectangularMagazine) //x, y are width, length, z is height
                    ScaleTransform.localScale = new Vector3((bulletScale.x * rowCount) + 0.05f, (bulletScale.x * Mathf.CeilToInt(ammoCapacity / rowCount)) + 0.05f, bulletScale.y + 0.05f);
                else //x is length, y/z are dia
                    ScaleTransform.localScale = new Vector3(bulletScale.x + 0.05f, bulletScale.x + 0.05f, (Mathf.CeilToInt(ammoCapacity / 36) * bulletScale.y) + 0.05f + bulletScale.y);
                using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                    while (sym.MoveNext())
                    {
                        if (sym.Current == null) continue;
                        var mam = sym.Current.FindModuleImplementing<ModuleAmmoMagazine>();
                        if (mam == null) continue;
                        mam.bulletScale = bulletScale;
                        mam.rowCount = rowCount;
                        mam.UpdateScaling(bulletScale);
                    }
                UpdateStackNode();
                StartCoroutine(AmmoVolumeChanged());
            }
        }

        public void UpdateScaling(Vector2 scale)
        {
            if (ScaleTransform != null)
            {
                if (isRectangularMagazine) //x, y are width, length, z is height
                    ScaleTransform.localScale = new Vector3((bulletScale.x * rowCount) + 0.05f, (bulletScale.x * Mathf.CeilToInt(ammoCapacity / rowCount)) + 0.05f, bulletScale.y + 0.05f);
                else //x is length, y/z are dia
                    ScaleTransform.localScale = new Vector3(bulletScale.x + 0.05f, bulletScale.x + 0.05f, (Mathf.CeilToInt(ammoCapacity / 36) * bulletScale.y) + 0.05f + bulletScale.y);
            }
            DragCube DragCube = DragCubeSystem.Instance.RenderProceduralDragCube(part);
            part.DragCubes.Procedural = true;
            part.DragCubes.ClearCubes();
            part.DragCubes.Cubes.Add(DragCube);
            part.DragCubes.ResetCubeWeights();
            part.DragCubes.ForceUpdate(true, true, false);
            part.DragCubes.SetDragWeights();

            UpdateStackNode();
            StartCoroutine(AmmoVolumeChanged());
        }

        public void UpdateStackNode()
        {
            using (List<AttachNode>.Enumerator stackNode = part.attachNodes.GetEnumerator())
                while (stackNode.MoveNext())
                {
                    if (stackNode.Current?.nodeType != AttachNode.NodeType.Stack ||
                        !originalStackNodePosition.ContainsKey(stackNode.Current.id)) continue;
                    if (isRectangularMagazine) continue;
                    if (stackNode.Current.id == "top" || stackNode.Current.id == "bottom")
                    {
                        Vector3 prevPos = stackNode.Current.position;                    
                        if (stackNode.Current.id == "top")
                        {
                            stackNode.Current.position.y = originalStackNodePosition[stackNode.Current.id].y + ScaleTransform.localScale.z / 2;
                            MoveParts(stackNode.Current, stackNode.Current.position - prevPos);
                        }
                        else
                        {
                            stackNode.Current.position.y = originalStackNodePosition[stackNode.Current.id].y - ScaleTransform.localScale.z / 2;
                            MoveParts(stackNode.Current, stackNode.Current.position - prevPos);                         
                        }
                    }
                    
                }
        }
        public void MoveParts(AttachNode node, Vector3 delta)
        {
            if (node.attachedPart is Part pushTarget)
            {
                if (pushTarget == null) return;
                Vector3 worldDelta = part.transform.TransformVector(delta);
                pushTarget.transform.position += worldDelta;
            }
        }

        IEnumerator AmmoVolumeChanged()
        {
            var wait = new WaitForSecondsFixed(0.25f);            
            ammoMass = $"{ammoCapacity * ammoResource.info.density * 1000} kg";
            ammoResource.maxAmount = ammoCapacity;
            ammoResource.amount = ammoCapacity; // Math.Min(resource.Current.amount, resource.Current.maxAmount);
            yield return wait;
            if (PAW != null) PAW.displayDirty = true;
            //GUIUtils.RefreshAssociatedWindows(part); //doesn't catch resource slider changes...?
        }
        private UIPartActionWindow _PAW = null;

        private UIPartActionWindow PAW
        {
            get
            {
                if (_PAW == null)
                {
                    _PAW = part.PartActionWindow;
                }
                return _PAW;
            }
        }

        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.AppendLine($"Ammo Magazine");
            output.AppendLine($"- Maximum Ordnance: {maxAmmo}");
            return output.ToString();
        }
    }
}
