using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System;
using TMPro;
using UniLinq;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine;

namespace BDArmory.Utils
{
    /// <summary>
    /// Logarithmic FloatRange slider.
    /// Based on https://github.com/meirumeiru/InfernalRobotics/blob/develop/InfernalRobotics/InfernalRobotics/Gui/UIPartActionFloatEditEx.cs
    /// I'm not entirely sure how much of this is necessary.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class UI_FloatLogRange : UI_FloatRange
    {
        private const string UIControlName = "FloatLogRange";
        public int steps = 10;
        public UI_FloatLogRange() { }
    }

    [UI_FloatLogRange]
    public class UIPartActionFloatLogRange : UIPartActionFieldItem
    {
        protected UI_FloatLogRange logFloatRange { get { return (UI_FloatLogRange)control; } }
        public TextMeshProUGUI fieldName;
        public TextMeshProUGUI fieldValue;
        public Slider slider;
        private float sliderStepSize;
        private bool blockSliderUpdate;
        private bool numericSliders = false;
        public GameObject numericContainer;
        public TextMeshProUGUI fieldNameNumeric;
        public TMP_InputField inputField;
        private float lastDisplayedValue = 0;

        public static Type VersionTaggedType(Type baseClass)
        {
            var ass = baseClass.Assembly;
            Type tagged = ass.GetTypes().Where(t => t.BaseType == baseClass).Where(t => t.FullName.StartsWith(baseClass.FullName)).FirstOrDefault();
            if (tagged != null)
                return tagged;
            return baseClass;
        }

        internal static T GetTaggedComponent<T>(GameObject gameObject) where T : Component
        {
            return (T)gameObject.GetComponent(VersionTaggedType(typeof(T)));
        }

        public static void InstantiateRecursive2(GameObject go, GameObject goc, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                list.Add(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject, ref list);
            }
        }

        public static void InstantiateRecursive(GameObject go, Transform trfp, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject goc = Instantiate(go.transform.GetChild(i).gameObject);
                goc.transform.parent = trfp;
                goc.transform.localPosition = go.transform.GetChild(i).localPosition;
                if ((goc.transform is RectTransform) && (go.transform.GetChild(i) is RectTransform))
                {
                    RectTransform rtc = goc.transform as RectTransform;
                    RectTransform rt = go.transform.GetChild(i) as RectTransform;

                    rtc.offsetMax = rt.offsetMax;
                    rtc.offsetMin = rt.offsetMin;
                }
                list.Add(go.transform.GetChild(i).gameObject, goc);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc, ref list);
            }
        }

        public static UIPartActionFloatLogRange CreateTemplate()
        {
            // Create the control
            GameObject gameObject = new GameObject("UIPartActionFloatLogRange", VersionTaggedType(typeof(UIPartActionFloatLogRange)));
            UIPartActionFloatLogRange partActionFloatLogRange = GetTaggedComponent<UIPartActionFloatLogRange>(gameObject);
            gameObject.SetActive(false);

            // Find the template for FloatRange
            UIPartActionFloatRange partActionFloatRange = (UIPartActionFloatRange)UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange));

            // Copy UI elements
            RectTransform rtc = gameObject.AddComponent<RectTransform>();
            RectTransform rt = partActionFloatRange.transform as RectTransform;
            rtc.offsetMin = rt.offsetMin;
            rtc.offsetMax = rt.offsetMax;
            rtc.anchorMin = rt.anchorMin;
            rtc.anchorMax = rt.anchorMax;
            LayoutElement lec = gameObject.AddComponent<LayoutElement>();
            LayoutElement le = partActionFloatRange.GetComponent<LayoutElement>();
            lec.flexibleHeight = le.flexibleHeight;
            lec.flexibleWidth = le.flexibleWidth;
            lec.minHeight = le.minHeight;
            lec.minWidth = le.minWidth;
            lec.preferredHeight = le.preferredHeight;
            lec.preferredWidth = le.preferredWidth;
            lec.layoutPriority = le.layoutPriority;

            // Copy control elements
            Dictionary<GameObject, GameObject> list = new Dictionary<GameObject, GameObject>();
            InstantiateRecursive(partActionFloatRange.gameObject, gameObject.transform, ref list);
            list.TryGetValue(partActionFloatRange.fieldName.gameObject, out GameObject fieldNameGO);
            partActionFloatLogRange.fieldName = fieldNameGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.fieldAmount.gameObject, out GameObject fieldValueGO);
            partActionFloatLogRange.fieldValue = fieldValueGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.slider.gameObject, out GameObject sliderGO);
            partActionFloatLogRange.slider = sliderGO.GetComponent<Slider>();
            list.TryGetValue(partActionFloatRange.numericContainer, out partActionFloatLogRange.numericContainer);
            list.TryGetValue(partActionFloatRange.inputField.gameObject, out GameObject inputFieldGO);
            partActionFloatLogRange.inputField = inputFieldGO.GetComponent<TMP_InputField>();
            list.TryGetValue(partActionFloatRange.fieldNameNumeric.gameObject, out GameObject fieldNameNumericGO);
            partActionFloatLogRange.fieldNameNumeric = fieldNameNumericGO.GetComponent<TextMeshProUGUI>();

            return partActionFloatLogRange;
        }

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);
            slider.minValue = Mathf.Log10(logFloatRange.minValue);
            slider.maxValue = Mathf.Log10(logFloatRange.maxValue);
            sliderStepSize = (slider.maxValue - slider.minValue) / logFloatRange.steps;
            logFloatRange.stepIncrement = sliderStepSize;
            fieldName.text = field.guiName;
            fieldNameNumeric.text = field.guiName;
            float value = GetFieldValue();
            value = UpdateSlider(value);
            SetFieldValue(value);
            UpdateDisplay(value);
            // Debug.Log($"DEBUG value is {value} with limits {logFloatRange.minValue}—{logFloatRange.maxValue}");
            // Debug.Log($"DEBUG slider has value {slider.value} with limits {slider.minValue}—{slider.maxValue}");
            slider.onValueChanged.AddListener(OnValueChanged);
            inputField.onSubmit.AddListener(OnNumericSubmitted);
        }

        private float GetFieldValue()
        {
            float value = field.GetValue<float>(field.host);
            return value;
        }
        private float UpdateSlider(float value)
        {
            // Note: We use Log10 here as it has better human-centric rounding properties (i.e., 0.001 instead of 0.000999999999).
            value = Mathf.Pow(10f, Mathf.Clamp(BDAMath.RoundToUnit(Mathf.Log10(value) - slider.minValue, sliderStepSize) + slider.minValue, slider.minValue, slider.maxValue));
            // Debug.Log($"DEBUG Slider updated to {value}");
            return value;
        }
        private void UpdateDisplay(float value)
        {
            if (numericSliders != Window.NumericSliders)
            {
                numericSliders = Window.NumericSliders;
                slider.gameObject.SetActive(!Window.NumericSliders);
                numericContainer.SetActive(Window.NumericSliders);
            }
            blockSliderUpdate = true;
            lastDisplayedValue = value;
            fieldValue.text = value.ToString("G3");
            if (numericSliders)
            { inputField.text = fieldValue.text; }
            else
            { slider.value = Mathf.Log10(value); }
            blockSliderUpdate = false;
        }
        private void OnValueChanged(float obj)
        {
            if (blockSliderUpdate) return;
            if (control is not null && control.requireFullControl)
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_FULLONLY)) return; }
            else
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_ANYCONTROL)) return; }
            float value = Mathf.Pow(10f, slider.value);
            value = UpdateSlider(value);
            SetFieldValue(value);
            UpdateDisplay(value);
        }
        private void OnNumericSubmitted(string str)
        {
            if (float.TryParse(str, out float value))
            {
                value = UpdateSlider(value);
                SetFieldValue(value);
                UpdateDisplay(value);
            }
        }
        public override void UpdateItem()
        {
            float value = GetFieldValue();
            if (value == lastDisplayedValue && numericSliders == Window.NumericSliders) return; // Do nothing if the value hasn't changed or the # hasn't been toggled.
            // fieldName.text = field.guiName; // Label doesn't update.
            UpdateDisplay(value);
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class UIPartActionFloatLogRangeRegistration : MonoBehaviour
    {
        private static bool loaded = false;
        private static bool isRunning = false;
        private Coroutine register = null;
        public void Start()
        {
            if (loaded)
            {
                Destroy(gameObject);
                return;
            }
            loaded = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnLevelFinishedLoading;
        }

        public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode)
        {
            if (isRunning) StopCoroutine("Register");
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;
            isRunning = true;
            register = StartCoroutine(Register());
        }

        internal IEnumerator Register()
        {
            UIPartActionController controller;
            while ((controller = UIPartActionController.Instance) is null) yield return null;

            FieldInfo typesField = (from fld in controller.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                    where fld.FieldType == typeof(List<Type>)
                                    select fld).First();

            List<Type> fieldPrefabTypes;
            while ((fieldPrefabTypes = (List<Type>)typesField.GetValue(controller)) == null
                || fieldPrefabTypes.Count == 0
                || !UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange)))
                yield return false;

            // Register prefabs
            controller.fieldPrefabs.Add(UIPartActionFloatLogRange.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_FloatLogRange));

            isRunning = false;
        }
    }
}
