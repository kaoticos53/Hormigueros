using System.Collections.Generic;
using UnityEngine;

namespace AntSim.Unity.Scripts.Presenter
{
    /// <summary>
    /// Picker de pools (F4.5): descarga el JSON canónico (<c>--mode presets --json</c>),
    /// lo parsea con el modelo puro y expone los dos niveles del selector —
    /// recomendados (los 4 del diseño de UX) y especialistas (con su ⚠ TradeOff).
    /// La UI (uGUI en F4.2) solo consume aquí; nuncahardcodea datos de pools.
    /// </summary>
    public sealed class PoolPickerBehaviour : MonoBehaviour
    {
        public string CliPath = "build/antsim";

        private Streaming.PoolPickerModel? _model;
        private Streaming.StreamSource? _source;

        public IReadOnlyList<Streaming.PoolPickerModel.Preset> Recommended { get; private set; }
            = new List<Streaming.PoolPickerModel.Preset>();

        public IReadOnlyList<Streaming.PoolPickerModel.Preset> Specialists { get; private set; }
            = new List<Streaming.PoolPickerModel.Preset>();

        private void Start()
        {
            _source = new Streaming.StreamSource(CliPath);
            _model = Streaming.PoolPickerModel.ParseJson(_source.FetchPresetsJson());

            var rec = new List<Streaming.PoolPickerModel.Preset>();
            foreach (var p in _model.Recommended()) rec.Add(p);
            Recommended = rec;

            var spec = new List<Streaming.PoolPickerModel.Preset>();
            foreach (var p in _model.Specialists()) spec.Add(p);
            Specialists = spec;
        }

        /// <summary>Comando de reproducción de un preset (la UI lo muestra tal cual).</summary>
        public string ReproFor(string presetId)
        {
            if (_model == null) return "";
            foreach (var p in _model.Presets)
                if (p.Id == presetId) return p.ReproCommand;
            return "";
        }

        /// <summary>F4.3: siembra el juego desde un preset — devuelve la ruta del
        /// .antgenome canónico para pasarla al SimPresenterBehaviour.SeedPoolPath
        /// (extraída del repro canónico, no hardcodeada en la UI).</summary>
        public string? SeedPoolFor(string presetId)
        {
            if (_model == null) return null;
            foreach (var p in _model.Presets)
                if (p.Id == presetId) return p.ResolveSeedPoolPath();
            return null;
        }
    }
}
