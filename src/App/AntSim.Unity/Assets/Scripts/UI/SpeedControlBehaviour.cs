using UnityEngine;
using UnityEngine.UI;
using AntSim.Unity.Scripts.Presenter;
using AntSim.Unity.Scripts.Streaming;

namespace AntSim.Unity.Scripts.UI
{
    /// <summary>
    /// Controlador interactivo en tiempo de ejecución para el panel de velocidad uGUI.
    /// Garantiza que todos los listeners (Slider, botones de paso +/-, pausa y presets)
    /// se registren correctamente al iniciar en Play Mode (evitando la pérdida de delegates
    /// anónimos que ocurre durante la serialización del editor de Unity).
    /// </summary>
    public sealed class SpeedControlBehaviour : MonoBehaviour
    {
        [Header("Referencias")]
        public SimPresenterBehaviour? Presenter;
        public Slider? SpeedSlider;
        public Text? SpeedLabel;
        public Button? PauseButton;
        public Text? PauseButtonText;
        public Button? MinusButton;
        public Button? PlusButton;
        public Button? Preset1;
        public Button? Preset5;
        public Button? Preset10;
        public Button? Preset25;
        public Button? Preset50;
        public Button? Preset100;
        public Button? NextGenButton;
        // Compatibilidad con prefijos anteriores
        public Button? Preset30;
        public Button? Preset300;
        public Button? Preset1000;

        private bool _isUpdatingSlider;

        private void Awake()
        {
            EnsureEventSystem();
            AutoWireComponents();
            DisableTextRaycasts();
        }

        private void Start()
        {
            if (Presenter == null)
                Presenter = Object.FindAnyObjectByType<SimPresenterBehaviour>();

            RegisterRuntimeListeners();
        }

        private void Update()
        {
            SyncUI();
        }

        /// <summary>
        /// Asegura que exista un EventSystem con StandaloneInputModule en la escena.
        /// Sin esto, ningún botón o slider de Unity uGUI recibe eventos de ratón.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current == null &&
                Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        /// <summary>
        /// Busca y asigna automáticamente los componentes hijos si no fueron seteados en el inspector.
        /// </summary>
        public void AutoWireComponents()
        {
            if (SpeedSlider == null) SpeedSlider = GetComponentInChildren<Slider>(true);
            if (PauseButton == null)
            {
                var t = transform.Find("PauseBtn");
                if (t != null) PauseButton = t.GetComponent<Button>();
            }
            if (PauseButton != null && PauseButtonText == null)
            {
                PauseButtonText = PauseButton.GetComponentInChildren<Text>(true);
            }
            if (MinusButton == null)
            {
                var t = transform.Find("SpeedMinusBtn");
                if (t != null) MinusButton = t.GetComponent<Button>();
            }
            if (PlusButton == null)
            {
                var t = transform.Find("SpeedPlusBtn");
                if (t != null) PlusButton = t.GetComponent<Button>();
            }
            if (SpeedLabel == null)
            {
                var t = transform.Find("SpeedLabel");
                if (t != null) SpeedLabel = t.GetComponent<Text>();
            }
            if (Preset1 == null)
            {
                var t = transform.Find("Preset_1") ?? transform.Find("Preset_100");
                if (t != null) Preset1 = t.GetComponent<Button>();
            }
            if (Preset5 == null)
            {
                var t = transform.Find("Preset_5");
                if (t != null) Preset5 = t.GetComponent<Button>();
            }
            if (Preset10 == null)
            {
                var t = transform.Find("Preset_10") ?? transform.Find("Preset_1000");
                if (t != null) Preset10 = t.GetComponent<Button>();
            }
            if (Preset25 == null)
            {
                var t = transform.Find("Preset_25");
                if (t != null) Preset25 = t.GetComponent<Button>();
            }
            if (Preset50 == null)
            {
                var t = transform.Find("Preset_50");
                if (t != null) Preset50 = t.GetComponent<Button>();
            }
            if (Preset100 == null)
            {
                var t = transform.Find("Preset_100") ?? transform.Find("Preset_100X");
                if (t != null) Preset100 = t.GetComponent<Button>();
            }
            if (NextGenButton == null)
            {
                var t = transform.Find("NextGenBtn");
                if (t != null) NextGenButton = t.GetComponent<Button>();
            }
        }

        /// <summary>
        /// Desactiva raycastTarget en todos los textos de los botones para evitar que intercepten
        /// clicks dirigidos al botón padre.
        /// </summary>
        private void DisableTextRaycasts()
        {
            foreach (var txt in GetComponentsInChildren<Text>(true))
            {
                txt.raycastTarget = false;
            }
        }

        /// <summary>
        /// Registra en runtime todos los eventos de interacción.
        /// </summary>
        public void RegisterRuntimeListeners()
        {
            if (SpeedSlider != null)
            {
                SpeedSlider.minValue = SpeedControlModel.MinSpeed;
                SpeedSlider.maxValue = SpeedControlModel.MaxSpeed;
                if (Presenter != null)
                    SpeedSlider.value = Presenter.Speed > 0f ? Presenter.Speed : SpeedControlModel.DefaultSpeed;

                SpeedSlider.onValueChanged.RemoveAllListeners();
                SpeedSlider.onValueChanged.AddListener(val =>
                {
                    if (_isUpdatingSlider || Presenter == null) return;
                    Presenter.SetSpeed(val);
                });
            }

            if (PauseButton != null)
            {
                PauseButton.onClick.RemoveAllListeners();
                PauseButton.onClick.AddListener(() =>
                {
                    if (Presenter != null) Presenter.TogglePause();
                });
            }

            if (NextGenButton != null)
            {
                NextGenButton.onClick.RemoveAllListeners();
                NextGenButton.onClick.AddListener(() =>
                {
                    if (Presenter != null) Presenter.TriggerNextGeneration();
                });
            }

            if (MinusButton != null)
            {
                MinusButton.onClick.RemoveAllListeners();
                MinusButton.onClick.AddListener(() =>
                {
                    if (Presenter != null) Presenter.StepSpeedDown();
                });
            }

            if (PlusButton != null)
            {
                PlusButton.onClick.RemoveAllListeners();
                PlusButton.onClick.AddListener(() =>
                {
                    if (Presenter != null) Presenter.StepSpeedUp();
                });
            }

            void WirePreset(Button? btn, float speed)
            {
                if (btn == null) return;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    if (Presenter != null) Presenter.SetSpeed(speed);
                });
            }

            WirePreset(Preset1, 1.00f);
            WirePreset(Preset5, 5.00f);
            WirePreset(Preset10, 10.00f);
            WirePreset(Preset25, 25.00f);
            WirePreset(Preset50, 50.00f);
            WirePreset(Preset100, 100.00f);
            WirePreset(Preset30, 1.00f);
            WirePreset(Preset300, 25.00f);
            WirePreset(Preset1000, 100.00f);
        }

        /// <summary>
        /// Sincroniza el estado del slider, etiqueta de texto y botón de pausa con la velocidad actual.
        /// </summary>
        private void SyncUI()
        {
            if (Presenter == null) return;
            float currentSpeed = Presenter.Speed;

            if (SpeedSlider != null && currentSpeed > 0f && Mathf.Abs(SpeedSlider.value - currentSpeed) > 0.005f)
            {
                _isUpdatingSlider = true;
                SpeedSlider.value = currentSpeed;
                _isUpdatingSlider = false;
            }

            if (SpeedLabel != null)
            {
                string formatted = SpeedControlModel.FormatSpeed(currentSpeed);
                if (SpeedLabel.text != formatted) SpeedLabel.text = formatted;
            }

            if (PauseButtonText != null)
            {
                string pauseLabel = currentSpeed <= 0f ? "\u25b6 Reanudar" : "\u23f8 Pausa";
                if (PauseButtonText.text != pauseLabel) PauseButtonText.text = pauseLabel;
            }
        }
    }
}
