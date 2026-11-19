using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Runtime.Scripts.Hud;
using Runtime.Scripts.Player;
using Runtime.Scripts.Propulsion;
using Runtime.Scripts.Shields;

namespace Runtime.Scripts.Skills
{
    public enum MaskKind { None, Range, Melee }

    // Rango: 0-3, Melee: 4-7. Nodo i de la mascara = (int)skill % 4, se desbloquea al nivel 2 * (i + 1).
    public enum Skill { Visor, Jetpack, Scope, Tracking, Triple, Shield, Hook, Slam }

    /// <summary>
    /// Progresion de habilidades por mascara (va en el player, junto a HudController).
    /// Desbloqueo por nivel del jugador; solo cuenta la mascara equipada. Incluye zoom por pasos y golpe triple.
    /// Los demas controladores solo consultan SkillProgression.Can(this, Skill.X) (true si no hay SkillProgression: enemigos).
    /// </summary>
    public class SkillProgression : NetworkBehaviour
    {
        public const int Count = 4;
        static readonly float[] Fov = { 40f, 25f, 12f };

        static readonly string[] Names =
        {
            "Visor", "Jetpack", "Mira telesc�pica", "Balas rastreadoras",
            "Golpe triple", "Escudo", "Gancho", "Ground slam"
        };

        static readonly string[] Descs =
        {
            "Visor t�ctico: resalta el calor de los enemigos, muestra su vida, nivel y equipo, y marca los objetos cercanos.",
            "Segundo salto en el aire: activa el jetpack. Manten el salto para volar; consume combustible, que se recarga en el suelo.",
            "Apunta con clic derecho y pulsa 3. La rueda del rat�n cambia el aumento (3 pasos).",
            "Disparar desde la cadera gu�a las balas hacia el enemigo m�s cercano. Apuntando vuelan rectas.",
            "Cada tercer golpe acertado seguido hace el doble de da�o. Fallar reinicia la cuenta.",
            "Mant�n clic derecho en modo melee para bloquear el da�o. Se agota y se recarga.",
            "Tecla F: engancha a un enemigo y lo arrastra hacia ti aturdido, o te impulsa hacia el suelo.",
            "Mant�n clic en el aire para caer en picada. Da�a y empuja a los enemigos en un radio."
        };

        static readonly Sprite[] s_icons = new Sprite[8];

        public static SkillProgression Local;

        readonly NetworkVariable<int> m_Net = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        MaskKind m_Local;
        HudController m_Hud;
        PlayerController m_Pc;
        PropulsionController m_Jet;
        ShieldsController m_Shield;
        int m_Key = -1, m_Zoom, m_Streak;
        float m_Tick;

        bool Mine => !IsSpawned || IsOwner;

        // ---------------- API ----------------

        public MaskKind Active => (MaskKind)(IsSpawned ? m_Net.Value : (int)m_Local);

        public int Level
        {
            get
            {
                if (m_Hud == null) m_Hud = GetComponent<HudController>();
                return m_Hud != null ? m_Hud.Level : 1;
            }
        }

        public static int Req(int i) => 2 * (i + 1);

        public bool Unlocked(int i) => Active != MaskKind.None && Level >= Req(i);

        public bool Has(Skill s) => (int)Active == (int)s / 4 + 1 && Level >= Req((int)s % 4);

        public static bool Can(Component c, Skill s)
        {
            var p = c.GetComponentInParent<SkillProgression>();
            return p == null || p.Has(s);
        }

        public void SetMask(MaskKind k)
        {
            m_Local = k;
            if (IsSpawned && IsOwner) m_Net.Value = (int)k;
            m_Key = -1;
        }

        // Golpe triple: MeleeController pide el multiplicador al empezar el golpe y reporta si acerto.
        public float SwingMul() => Has(Skill.Triple) && m_Streak == 2 ? 2f : 1f;
        public void SwingResult(bool landed) => m_Streak = landed ? (m_Streak + 1) % 3 : 0;

        public static string Name(MaskKind k, int i) => k == MaskKind.None ? "" : Names[((int)k - 1) * 4 + i];
        public static string Desc(MaskKind k, int i) => k == MaskKind.None ? "" : Descs[((int)k - 1) * 4 + i];

        public static Sprite Icon(MaskKind k, int i)
        {
            if (k == MaskKind.None) return null;
            int id = ((int)k - 1) * 4 + i;
            return s_icons[id] ??= Draw((Skill)id);
        }

        // ---------------- logica ----------------

        public override void OnNetworkSpawn()
        {
            if (IsOwner) m_Net.Value = (int)m_Local;
        }

        public override void OnDestroy()
        {
            if (Local == this) Local = null;
            base.OnDestroy();
        }

        void Update()
        {
            if (!Mine) return;
            if (Local != this) Local = this;
            if (m_Pc == null) m_Pc = GetComponent<PlayerController>();

            int key = Level * 4 + (int)Active;
            if (key != m_Key || Time.unscaledTime >= m_Tick)
            {
                m_Key = key;
                m_Tick = Time.unscaledTime + 0.5f;
                Apply();
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;
            Zoom();
        }

        // Interruptores de los controladores existentes. Se reaplica cada 0.5 s por si un item (jetpack, escudo) los reactiva.
        void Apply()
        {
            if (m_Jet == null) m_Jet = GetComponentInChildren<PropulsionController>(true);
            if (m_Shield == null) m_Shield = GetComponentInChildren<ShieldsController>(true);

            if (m_Jet != null) m_Jet.isUnlocked = Has(Skill.Jetpack);
            if (m_Shield != null)
            {
                bool on = Has(Skill.Shield);
                if (!on && m_Shield.isUnlocked) m_Shield.SetShieldState(false);
                m_Shield.isUnlocked = on;
            }
            if (m_Pc != null && !Has(Skill.Scope)) m_Pc.scopeMode = false;
        }

        void Zoom()
        {
            var m = Mouse.current;
            if (m_Pc == null || m == null || !m_Pc.scope) return;
            float s = m.scroll.ReadValue().y;
            if (s > 0f) m_Zoom = Mathf.Min(m_Zoom + 1, Fov.Length - 1);
            else if (s < 0f) m_Zoom = Mathf.Max(m_Zoom - 1, 0);
            m_Pc.aimFOV = Fov[m_Zoom];
        }

        // ---------------- iconos (dibujados por codigo, una vez) ----------------

        static Sprite Draw(Skill s)
        {
            const int N = 64;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Shape(s, x, y)) * 255f));
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        static float Shape(Skill s, float x, float y)
        {
            float dx = x - 31.5f, dy = y - 31.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
            switch (s)
            {
                case Skill.Visor:
                    {
                        float e = Mathf.Sqrt(dx * dx / 3.45f + dy * dy);
                        return Mathf.Max(Band(e, 11f, 14f), Disc(x, y, 31.5f, 31.5f, 5f));
                    }
                case Skill.Jetpack:
                    {
                        float a = Mathf.Max(Box(x, y, 17f, 16f, 29f, 48f), Box(x, y, 34f, 16f, 46f, 48f));
                        a = Mathf.Max(a, Box(x, y, 29f, 30f, 34f, 36f));
                        return Mathf.Max(a, Mathf.Max(Disc(x, y, 23f, 11f, 4f), Disc(x, y, 40f, 11f, 4f)));
                    }
                case Skill.Scope:
                    {
                        float a = Band(d, 17f, 21f);
                        a = Mathf.Max(a, Mathf.Clamp01(2f - ax) * Band(ay, 6f, 28f));
                        a = Mathf.Max(a, Mathf.Clamp01(2f - ay) * Band(ax, 6f, 28f));
                        return Mathf.Max(a, Mathf.Clamp01(2.5f - d));
                    }
                case Skill.Tracking:
                    {
                        float a = Mathf.Max(Band(d, 11f, 14f), Disc(x, y, 31.5f, 31.5f, 4f));
                        a = Mathf.Max(a, Mathf.Clamp01(2f - ax) * Band(ay, 18f, 28f));
                        return Mathf.Max(a, Mathf.Clamp01(2f - ay) * Band(ax, 18f, 28f));
                    }
                case Skill.Triple:
                    return Mathf.Max(Box(x, y, 10f, 14f, 22f, 28f), Mathf.Max(Box(x, y, 26f, 14f, 38f, 40f), Box(x, y, 42f, 14f, 54f, 52f)));
                case Skill.Shield:
                    {
                        float hw = y >= 36f ? 18f : 18f * Mathf.Clamp01((y - 10f) / 26f);
                        if (hw <= 0f) return 0f;
                        float a = Band(x, 31.5f - hw, 31.5f + hw) * Band(y, 10f, 54f);
                        return a * (1f - Band(ax, 0f, 1.5f) * Band(y, 16f, 48f) * 0.6f);
                    }
                case Skill.Hook:
                    {
                        float px = x - 32f, py = y - 50f, hx = x - 22f, hy = y - 22f;
                        float a = Mathf.Max(Band(Mathf.Sqrt(px * px + py * py), 5f, 9f), Box(x, y, 30f, 22f, 34f, 41f));
                        if (y <= 22f && x <= 34f) a = Mathf.Max(a, Band(Mathf.Sqrt(hx * hx + hy * hy), 8f, 12f));
                        return Mathf.Max(a, Box(x, y, 10f, 22f, 14f, 30f));
                    }
                default: // Slam: flecha hacia abajo sobre el suelo
                    {
                        float head = Band(ax, 0f, (y - 8f) * 0.85f) * Band(y, 8f, 30f);
                        return Mathf.Max(Box(x, y, 28f, 28f, 35f, 56f), Mathf.Max(head, Box(x, y, 8f, 3f, 56f, 7f)));
                    }
            }
        }

        static float Band(float v, float a, float b) => Mathf.Clamp01(Mathf.Min(v - a, b - v) + 0.5f);
        static float Box(float x, float y, float x0, float y0, float x1, float y1) => Band(x, x0, x1) * Band(y, y0, y1);
        static float Disc(float x, float y, float cx, float cy, float r) => Mathf.Clamp01(r - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + 0.5f);
    }
}