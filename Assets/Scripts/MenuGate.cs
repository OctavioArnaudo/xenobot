using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Runtime.Scripts.Gate
{
    /// <summary>
    /// Un solo menú a la vez + Esc unificado.
    /// Cada menú pide paso con TryOpen("clave"), se suscribe a CloseRequested mientras está abierto y libera con Close("clave").
    /// Con inventario, crafting, stats o mapa abiertos, Esc se consume (el menú de pausa no lo ve) y cierra ese menú.
    /// </summary>
    public static class MenuGate
    {
        static string _owner;
        static bool _esc, _escDown;
        static int _escFrame = -1;

        /// <summary>Esc pulsado con un menú abierto: recibe la clave del menú que debe cerrarse.</summary>
        public static event Action<string> CloseRequested;

        public static bool Busy => _owner != null;

        /// <summary>Hay un menú que Esc debe cerrar (inventario, crafting, stats, mapa).</summary>
        public static bool HasClosableMenu => EscCloses;

        /// <summary>Esc ya se usó este frame para cerrar un menú.</summary>
        public static bool EscConsumed => _escFrame == Time.frameCount;

        /// <summary>Toma el control sin pedir permiso (menú de pausa: va por encima de todo).</summary>
        public static void Take(string key) => _owner = key;

        public static bool TryOpen(string key)
        {
            if (_owner != null && _owner != key) return false;
            _owner = key;
            return true;
        }

        public static void Close(string key)
        {
            if (_owner == key) _owner = null;
        }

        // Diálogos, consolas y el panel J gestionan su propio Esc (o ninguno): no se consume.
        static bool EscCloses => _owner != null && _owner != "dialog" && _owner != "story" && _owner != "mission";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _owner = null;
            _esc = _escDown = false;
            CloseRequested = null;
            SceneManager.sceneLoaded -= OnScene;
            SceneManager.sceneLoaded += OnScene;
            InputSystem.onEvent -= OnInput;
            InputSystem.onEvent += OnInput;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            var g = new GameObject("MenuGate") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(g);
            g.AddComponent<Runner>();
        }

        static void OnScene(Scene s, LoadSceneMode m)
        {
            _owner = null;
            CloseRequested = null;
        }

        // Se descarta la pulsación de Esc para todo el juego (incluido el menú de pausa) y se cierra el menú en Update.
        static void OnInput(InputEventPtr e, InputDevice d)
        {
            if (!(d is Keyboard k) || !(e.IsA<StateEvent>() || e.IsA<DeltaStateEvent>())) return;
            if (!k.escapeKey.ReadValueFromEvent(e, out float v)) return;
            if (v <= 0.5f) { _escDown = false; return; }
            if (_escDown) return;
            _escDown = true;
            if (!EscCloses) return;
            _esc = true;
            e.handled = true;
        }

        [DefaultExecutionOrder(-1000)]
        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                if (!_esc) return;
                _esc = false;
                _escFrame = Time.frameCount;
                if (_owner != null) CloseRequested?.Invoke(_owner);
            }
        }
    }
}