using ActionGame.Data;
using ActionGame.Sim;
using UnityEditor;
using UnityEngine;

namespace ActionGame.EditorTools
{
    /// <summary>
    /// **攻击判定编辑器**（块 T2）：逐帧预览攻击动画 + 在 Scene 视图里可视化编辑 hitbox。
    ///
    /// 工作流：
    ///   拖入招式资产（<see cref="AttackDataAsset"/>）和预览模型（FBX / prefab）→
    ///   点 <c>Create Preview</c> 在场景里生成一个**不落盘**的预览副本 →
    ///   拖时间轴 / 播放逐帧看动画 → 在 Scene 视图里拖胶囊端点、调半径、增删 hitbox →
    ///   所有修改立刻写回资产（游戏 Play 时下一 tick 生效）。
    ///
    /// ---- 为什么是 Editor 工具而不是运行时功能 ----
    /// 它只**编辑数据资产**：不跑模拟、不复制判定逻辑、不参与战斗。
    /// 几何换算沿用 Sim 的约定（脚底原点 / x=右 y=上 z=前 / 只绕 Y），
    /// 所以工具里看到的胶囊位置就是运行时判定的位置。
    ///
    /// ---- 命名空间为什么不是 ActionGame.Editor ----
    /// Unity 只把名字恰好叫 `Editor` 的文件夹当 Editor 程序集，所以目录必须是 `Editor/`；
    /// 但 `ActionGame.Editor` 会和 `UnityEditor.Editor`（类）在 `using UnityEditor;` 下打架，
    /// 命名空间因此取 `ActionGame.EditorTools`。
    ///
    /// ---- 预览对象 ----
    /// 场景里实例化 `HideFlags.HideAndDontSave` 的副本：不存盘、不进层级。
    /// 域重载（脚本编译）不会自动清掉场景对象，所以 `OnDisable` / 编译前 / 退出编辑器 /
    /// 进 Play 时都显式销毁。场景可能显示 dirty 标记，但存盘不会包含预览。
    ///
    /// ---- 资产编辑 ----
    /// 走 `SerializedObject`（路径 `_data._hitboxes`），不给运行时加公开 setter——
    /// 「Sim 拿不到可写路径」的值语义不能为了工具破掉。
    /// </summary>
    public class AttackHitboxEditor : EditorWindow
    {
        private const string MenuPath = "Tools/ActionGame/Attack Hitbox Editor";

        /// <summary>
        /// 预览朝向。本地坐标与朝向无关（hitbox 定义在角色本地空间里），
        /// 预览固定 yaw = 0（模型朝 +Z），这样 Scene 视图里的本地轴就是世界轴。
        /// </summary>
        private const float PreviewYawDegrees = 0f;

        [SerializeField] private AttackDataAsset _attackData;
        [SerializeField] private GameObject _previewSource;
        [SerializeField] private Vector3 _previewPosition = Vector3.zero;
        [SerializeField] private int _tick;
        [SerializeField] private int _selectedHitbox = -1;

        // 预览对象不序列化：域重载后场景里的副本已经销毁，字段按需重建。
        private GameObject _preview;
        private bool _playing;
        private double _playAccumulator;
        private double _lastUpdateTime;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            AttackHitboxEditor window = GetWindow<AttackHitboxEditor>("Attack Hitbox");
            window.minSize = new Vector2(430f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyPreview;
            EditorApplication.quitting += DestroyPreview;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyPreview;
            EditorApplication.quitting -= DestroyPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            StopPlayback();
            DestroyPreview();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            // 进 Play 之前就销毁：Play 里工具不工作，避免场景里出现两份模型。
            if (change == PlayModeStateChange.ExitingEditMode
                || change == PlayModeStateChange.EnteredPlayMode)
            {
                StopPlayback();
                DestroyPreview();
            }
        }

        // =====================================================================
        // 窗口
        // =====================================================================

        private void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "攻击判定编辑器只在非 Play 模式下使用。退出 Play 后重新 Create Preview。",
                    MessageType.Info);
                return;
            }

            DrawAssetSection();
            DrawTimeline();
            DrawHitboxList();
        }

        private void DrawAssetSection()
        {
            EditorGUI.BeginChangeCheck();
            AttackDataAsset newData = (AttackDataAsset)EditorGUILayout.ObjectField(
                "Attack Data", _attackData, typeof(AttackDataAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                _attackData = newData;
                _selectedHitbox = -1;
                ClampTick();
                SampleCurrentTick();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _previewSource = (GameObject)EditorGUILayout.ObjectField(
                "Preview Model", _previewSource, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                // 换模型：重建预览（旧的直接丢）。
                DestroyPreview();
            }

            EditorGUI.BeginChangeCheck();
            _previewPosition = EditorGUILayout.Vector3Field("Preview Position", _previewPosition);
            if (EditorGUI.EndChangeCheck() && _preview != null)
            {
                _preview.transform.position = _previewPosition;
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_previewSource == null))
                {
                    if (GUILayout.Button("Create Preview"))
                    {
                        EnsurePreview();
                    }
                }

                using (new EditorGUI.DisabledScope(_preview == null))
                {
                    if (GUILayout.Button("Destroy Preview"))
                    {
                        DestroyPreview();
                    }
                }
            }

            if (_attackData == null)
            {
                EditorGUILayout.HelpBox("先拖入一个 Attack Data 资产。", MessageType.Info);
            }
        }

        private void DrawTimeline()
        {
            int totalTicks = CurrentTotalTicks();
            int maxTick = Mathf.Max(0, totalTicks - 1);
            ClampTick();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"Timeline  {totalTicks} tick  ({totalTicks * SimConstants.FixedDeltaTime:F2} s)",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀", GUILayout.Width(28f))) { StepTo(_tick - 1); }

                int newTick = EditorGUILayout.IntSlider(_tick, 0, maxTick);
                if (newTick != _tick) { StepTo(newTick); }

                if (GUILayout.Button("▶", GUILayout.Width(28f))) { StepTo(_tick + 1); }

                if (GUILayout.Button(_playing ? "Stop" : "Play", GUILayout.Width(52f)))
                {
                    if (_playing) { StopPlayback(); }
                    else { StartPlayback(); }
                }
            }

            EditorGUILayout.LabelField(
                $"t {_tick} / {totalTicks}    ({(totalTicks > 0 ? _tick : 0) * SimConstants.FixedDeltaTime:F3} s)");
        }

        // =====================================================================
        // 时间轴
        // =====================================================================

        private int CurrentTotalTicks()
        {
            return _attackData != null ? _attackData.Data.TotalTicks : 0;
        }

        private void ClampTick()
        {
            int maxTick = Mathf.Max(0, CurrentTotalTicks() - 1);
            int clamped = Mathf.Clamp(_tick, 0, maxTick);

            // 外部把 TotalTicks 改小时，帧号和姿势一起跟上。
            if (clamped == _tick) { return; }
            _tick = clamped;
            SampleCurrentTick();
        }

        private void StepTo(int tick)
        {
            int maxTick = Mathf.Max(0, CurrentTotalTicks() - 1);
            _tick = Mathf.Clamp(tick, 0, maxTick);
            SampleCurrentTick();
            SceneView.RepaintAll();
            Repaint();
        }

        private void StartPlayback()
        {
            if (_playing || _attackData == null || CurrentTotalTicks() <= 0) { return; }

            // 从头播一遍；停在最后一帧，不循环（循环会看不清收招）。
            if (_tick >= CurrentTotalTicks() - 1) { _tick = 0; }

            _playing = true;
            _playAccumulator = 0.0;
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void StopPlayback()
        {
            if (!_playing) { return; }
            _playing = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!_playing) { return; }

            double now = EditorApplication.timeSinceStartup;
            _playAccumulator += now - _lastUpdateTime;
            _lastUpdateTime = now;

            int maxTick = Mathf.Max(0, CurrentTotalTicks() - 1);
            bool advanced = false;

            // 按模拟节拍（60Hz）推进；一帧里可能要补多个 tick（编辑器掉帧时）。
            while (_playAccumulator >= SimConstants.FixedDeltaTime)
            {
                _playAccumulator -= SimConstants.FixedDeltaTime;

                if (_tick >= maxTick)
                {
                    StopPlayback();
                    break;
                }

                _tick++;
                advanced = true;
            }

            if (!advanced) { return; }

            SampleCurrentTick();
            SceneView.RepaintAll();
            Repaint();
        }

        // =====================================================================
        // 预览对象
        // =====================================================================

        private void EnsurePreview()
        {
            if (_previewSource == null || EditorApplication.isPlayingOrWillChangePlaymode) { return; }

            DestroyPreview();

            _preview = Instantiate(_previewSource, _previewPosition, Quaternion.identity);
            _preview.name = "[AttackPreview]";

            // DontSave：不存盘；HideInHierarchy：层级里不碍事。
            foreach (Transform t in _preview.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            // 预览由 SampleAnimation 手动驱动，模型自带的 Animator 不要抢。
            Animator animator = _preview.GetComponentInChildren<Animator>(true);
            if (animator != null) { animator.enabled = false; }

            SampleCurrentTick();
            SceneView.RepaintAll();
        }

        private void DestroyPreview()
        {
            if (_preview == null) { return; }
            DestroyImmediate(_preview);
            _preview = null;
            SceneView.RepaintAll();
        }

        private void SampleCurrentTick()
        {
            if (_attackData == null || _preview == null) { return; }

            AnimationClip clip = _attackData.AnimationClip;
            if (clip == null) { return; }   // 留空是正当配置：模型停在绑定姿势

            // 第 T tick 采样 clip 的第 T 帧（60 fps）。和运行时 ActorAnimation 的相位一致。
            clip.SampleAnimation(_preview, _tick * SimConstants.FixedDeltaTime);
        }

        // =====================================================================
        // Scene 视图：画所有 hitbox + 选中项的手柄
        // =====================================================================

        private void OnSceneGUI(SceneView sceneView)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { return; }
            if (_preview == null || _attackData == null) { return; }

            Vector3 feet = _preview.transform.position;

            // 前向箭头：确认模型朝向 = 本地 +Z（hitbox 的 z 正方向）。
            Handles.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Handles.ArrowHandleCap(0, feet, Quaternion.identity, 0.8f, EventType.Repaint);

            SerializedObject serialized = new SerializedObject(_attackData);
            SerializedProperty data = serialized.FindProperty("_data");
            SerializedProperty boxes = data != null ? data.FindPropertyRelative("_hitboxes") : null;
            if (boxes == null || !boxes.isArray) { return; }

            for (int i = 0; i < boxes.arraySize; i++)
            {
                SerializedProperty element = boxes.GetArrayElementAtIndex(i);
                int start = element.FindPropertyRelative("StartTick").intValue;
                int end = element.FindPropertyRelative("EndTick").intValue;
                Vector3 localFrom = element.FindPropertyRelative("LocalFrom").vector3Value;
                Vector3 localTo = element.FindPropertyRelative("LocalTo").vector3Value;
                float radius = element.FindPropertyRelative("Radius").floatValue;

                Vector3 from = LocalToWorld(localFrom, feet);
                Vector3 to = LocalToWorld(localTo, feet);
                bool active = _tick >= start && _tick <= end;
                bool selected = i == _selectedHitbox;

                Handles.color = selected
                    ? new Color(1f, 0.6f, 0.1f, 1f)       // 选中：橙
                    : active
                        ? new Color(1f, 0.9f, 0.2f, 1f)   // 当前帧活跃：亮黄
                        : new Color(0.6f, 0.6f, 0.6f, 0.5f);  // 非活跃：灰

                DrawWireCapsule(from, to, radius);

                if (!selected) { continue; }

                // ---- 选中项的手柄：两个端点 + 半径 ----
                EditorGUI.BeginChangeCheck();
                Vector3 newFrom = Handles.PositionHandle(from, Quaternion.identity);
                Vector3 newTo = Handles.PositionHandle(to, Quaternion.identity);

                Vector3 axis = to - from;
                Quaternion discRotation = axis.sqrMagnitude > 1e-8f
                    ? Quaternion.LookRotation(axis)
                    : Quaternion.identity;
                float newRadius = Handles.RadiusHandle(discRotation, from, radius);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_attackData, "Edit Hitbox");
                    element.FindPropertyRelative("LocalFrom").vector3Value = WorldToLocal(newFrom, feet);
                    element.FindPropertyRelative("LocalTo").vector3Value = WorldToLocal(newTo, feet);
                    element.FindPropertyRelative("Radius").floatValue = Mathf.Max(0f, newRadius);
                    serialized.ApplyModifiedProperties();
                    Repaint();
                    SceneView.RepaintAll();
                }
            }
        }

        /// <summary>本地（脚底原点）→ 世界。与 <see cref="HitboxDefinition.LocalPointToWorld"/> 同一约定。</summary>
        private static Vector3 LocalToWorld(Vector3 local, Vector3 feet)
        {
            float rad = PreviewYawDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            float cos = Mathf.Cos(rad);

            return feet + new Vector3(
                local.x * cos + local.z * sin,
                local.y,
                -local.x * sin + local.z * cos);
        }

        /// <summary>世界 → 本地。上面那个的逆旋转（只绕 Y，逆 = 转置）。</summary>
        private static Vector3 WorldToLocal(Vector3 world, Vector3 feet)
        {
            Vector3 d = world - feet;
            float rad = PreviewYawDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            float cos = Mathf.Cos(rad);

            return new Vector3(
                d.x * cos - d.z * sin,
                d.y,
                d.x * sin + d.z * cos);
        }

        /// <summary>线框胶囊：两端圆盘 + 四条母线（和 HitboxGizmo 同一几何，改成 Handles 版本）。</summary>
        private static void DrawWireCapsule(Vector3 from, Vector3 to, float radius)
        {
            radius = Mathf.Max(0f, radius);   // 配错成负数时不至于画出一团怪东西
            Vector3 axis = to - from;

            if (axis.sqrMagnitude < 1e-8f)
            {
                // 退化成球：画三个正交圆盘就够了。
                Handles.DrawWireDisc(from, Vector3.up, radius);
                Handles.DrawWireDisc(from, Vector3.right, radius);
                Handles.DrawWireDisc(from, Vector3.forward, radius);
                return;
            }

            Vector3 dir = axis.normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f
                ? Vector3.right
                : Vector3.up;
            Vector3 u = Vector3.Cross(dir, reference).normalized;
            Vector3 v = Vector3.Cross(dir, u).normalized;

            Handles.DrawWireDisc(from, dir, radius);
            Handles.DrawWireDisc(to, dir, radius);

            Handles.DrawLine(from + u * radius, to + u * radius);
            Handles.DrawLine(from - u * radius, to - u * radius);
            Handles.DrawLine(from + v * radius, to + v * radius);
            Handles.DrawLine(from - v * radius, to - v * radius);
        }

        // =====================================================================
        // Hitbox 列表（SerializedObject 编辑）
        // =====================================================================

        private void DrawHitboxList()
        {
            if (_attackData == null) { return; }

            EditorGUILayout.Space();
            SerializedObject serialized = new SerializedObject(_attackData);
            SerializedProperty data = serialized.FindProperty("_data");
            SerializedProperty boxes = data != null ? data.FindPropertyRelative("_hitboxes") : null;

            if (boxes == null || !boxes.isArray)
            {
                EditorGUILayout.HelpBox(
                    "资产结构里找不到 `_data._hitboxes`——AttackData 的字段名可能变了。",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField($"Hitboxes ({boxes.arraySize})", EditorStyles.boldLabel);

            // 结构操作延迟到循环之后执行：避免边遍历边改数组。
            int deleteIndex = -1;
            int duplicateIndex = -1;
            bool anyChange = false;

            for (int i = 0; i < boxes.arraySize; i++)
            {
                SerializedProperty element = boxes.GetArrayElementAtIndex(i);
                SerializedProperty start = element.FindPropertyRelative("StartTick");
                SerializedProperty end = element.FindPropertyRelative("EndTick");
                SerializedProperty localFrom = element.FindPropertyRelative("LocalFrom");
                SerializedProperty localTo = element.FindPropertyRelative("LocalTo");
                SerializedProperty radius = element.FindPropertyRelative("Radius");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isSelected = _selectedHitbox == i;
                    bool nowSelected = GUILayout.Toggle(
                        isSelected, $"Hitbox {i}", "Button", GUILayout.Width(110f));
                    if (nowSelected != isSelected)
                    {
                        _selectedHitbox = i;
                        SceneView.RepaintAll();
                    }

                    bool active = _tick >= start.intValue && _tick <= end.intValue;
                    GUILayout.Label(active ? "● 当前帧" : "○", GUILayout.Width(64f));

                    if (GUILayout.Button("Duplicate", GUILayout.Width(82f)))
                    {
                        duplicateIndex = i;
                    }

                    if (GUILayout.Button("Delete", GUILayout.Width(56f)))
                    {
                        deleteIndex = i;
                    }
                }

                int newStart = EditorGUILayout.IntField("Start Tick", start.intValue);
                int newEnd = EditorGUILayout.IntField("End Tick", end.intValue);
                float newRadius = EditorGUILayout.FloatField("Radius", radius.floatValue);
                Vector3 newFrom = EditorGUILayout.Vector3Field("Local From", localFrom.vector3Value);
                Vector3 newTo = EditorGUILayout.Vector3Field("Local To", localTo.vector3Value);

                if (newStart != start.intValue
                    || newEnd != end.intValue
                    || !Mathf.Approximately(newRadius, radius.floatValue)
                    || newFrom != localFrom.vector3Value
                    || newTo != localTo.vector3Value)
                {
                    Undo.RecordObject(_attackData, "Edit Hitbox");
                    start.intValue = newStart;
                    end.intValue = newEnd;
                    radius.floatValue = newRadius;
                    localFrom.vector3Value = newFrom;
                    localTo.vector3Value = newTo;
                    anyChange = true;
                }

                EditorGUILayout.EndVertical();
            }

            if (deleteIndex >= 0)
            {
                Undo.RecordObject(_attackData, "Delete Hitbox");
                boxes.DeleteArrayElementAtIndex(deleteIndex);
                _selectedHitbox = -1;
                anyChange = true;
            }
            else if (duplicateIndex >= 0)
            {
                HitboxDefinition copy = ReadHitbox(boxes, duplicateIndex);
                Undo.RecordObject(_attackData, "Duplicate Hitbox");
                int newIndex = boxes.arraySize;
                boxes.InsertArrayElementAtIndex(newIndex);
                WriteHitbox(boxes.GetArrayElementAtIndex(newIndex), copy);
                _selectedHitbox = newIndex;
                anyChange = true;
            }

            if (GUILayout.Button("Add hitbox at current tick"))
            {
                HitboxDefinition hitbox = new HitboxDefinition
                {
                    StartTick = _tick,
                    EndTick = _tick,
                    LocalFrom = new Vector3(0f, 0.5f, 0.6f),
                    LocalTo = new Vector3(0f, 0.5f, 1.2f),
                    Radius = 0.5f,
                };

                Undo.RecordObject(_attackData, "Add Hitbox");
                int newIndex = boxes.arraySize;
                boxes.InsertArrayElementAtIndex(newIndex);
                WriteHitbox(boxes.GetArrayElementAtIndex(newIndex), hitbox);
                _selectedHitbox = newIndex;
                anyChange = true;
            }

            if (anyChange)
            {
                serialized.ApplyModifiedProperties();
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private static HitboxDefinition ReadHitbox(SerializedProperty boxes, int index)
        {
            SerializedProperty element = boxes.GetArrayElementAtIndex(index);
            return new HitboxDefinition
            {
                StartTick = element.FindPropertyRelative("StartTick").intValue,
                EndTick = element.FindPropertyRelative("EndTick").intValue,
                LocalFrom = element.FindPropertyRelative("LocalFrom").vector3Value,
                LocalTo = element.FindPropertyRelative("LocalTo").vector3Value,
                Radius = element.FindPropertyRelative("Radius").floatValue,
            };
        }

        private static void WriteHitbox(SerializedProperty element, HitboxDefinition hitbox)
        {
            element.FindPropertyRelative("StartTick").intValue = hitbox.StartTick;
            element.FindPropertyRelative("EndTick").intValue = hitbox.EndTick;
            element.FindPropertyRelative("LocalFrom").vector3Value = hitbox.LocalFrom;
            element.FindPropertyRelative("LocalTo").vector3Value = hitbox.LocalTo;
            element.FindPropertyRelative("Radius").floatValue = hitbox.Radius;
        }
    }
}
