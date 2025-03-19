using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

using FluffySpectre.UnityEventGraph.LayoutStrategies;
using FluffySpectre.UnityEventGraph.Utilities;

namespace FluffySpectre.UnityEventGraph 
{
    public class EventGraphWindow : EditorWindow
    {
        private EventGraphAnalyzer _analyzer;
        private EventGraphData _graphData;
        private EventGraphView _graphView;
        private EventInvocationListPanel _eventInvocationListPanel;
        private NodeGraphData _nodeGraphData;
        private ControlBar _controlBar;
        private bool _isRecording;
        private VisualElement _verticalContainer;
        private VisualElement _mainContainer;
        private bool _isPanelVisible = false;
        private Dictionary<UnityEventNode, double> _highlightedNodes = new();
        private Dictionary<UnityEventPort, double> _highlightedPorts = new();
        private Dictionary<Edge, double> _highlightedEdges = new();
        private GraphFilterManager _filterManager;
        private GameObject[] _lastAnalyzedGameObjects;
        private bool _isReferenceSearchEnabled = false;
        private bool _initialized = false;
        
        // Performance optimizations
        private double _lastHighlightTime = 0;
        private const double HIGHLIGHT_THROTTLE_INTERVAL = 0.1; // seconds
        private bool _isAnalyzing = false;
        private const int LARGE_GRAPH_THRESHOLD = 200;

        [MenuItem("Window/Event Graph")]
        public static void OpenWindow()
        {
            var window = GetWindow<EventGraphWindow>("Event Graph", true);
            window.Show();
        }

        private void OnEnable()
        {
            _verticalContainer = new VisualElement();
            _verticalContainer.style.flexDirection = FlexDirection.Column;
            _verticalContainer.style.flexGrow = 1;
            rootVisualElement.Add(_verticalContainer);

            _controlBar = new ControlBar();
            _controlBar.OnSaveGraph += SaveNodePositions;
            _controlBar.OnRefreshGraphView += () => AnalyzeFromSelectedGameObjects();
            _controlBar.OnRecord += () => ChangeRecordingStatus();
            _controlBar.OnTogglePanel += () => ToggleEventInvocationListPanel();
            _controlBar.OnLayoutStrategyChanged += OnLayoutStrategyChanged;
            _controlBar.OnFilterChanged += ToggleInvokedNodesFilter;
            _controlBar.OnReferenceSearchChanged += isEnabled =>
            {
                _isReferenceSearchEnabled = isEnabled;
            };
            _verticalContainer.Add(_controlBar);

            _mainContainer = new VisualElement();
            _mainContainer.style.flexDirection = FlexDirection.Row;
            _mainContainer.style.flexGrow = 1;
            _verticalContainer.Add(_mainContainer);

            _graphView = new EventGraphView
            {
                name = "Event Graph"
            };
            _graphView.style.flexGrow = 1;
            _mainContainer.Add(_graphView);

            _eventInvocationListPanel = new EventInvocationListPanel();
            _eventInvocationListPanel.OnEventButtonClicked += EventButtonClicked;
            _eventInvocationListPanel.OnClearList += ResetEventLog;
            _mainContainer.Add(_eventInvocationListPanel);

            ToggleEventInvocationListPanel(false);

            EventTracker.OnEventTracked += OnEventTracked;

            EditorApplication.update += OnEditorUpdate;

            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            _filterManager = new GraphFilterManager();

            LoadNodeGraphData();

            _analyzer = new EventGraphAnalyzer
            {
                SavedNodeGraphData = _nodeGraphData
            };
        }

        private void OnDisable()
        {
            SaveState();

            EventTracker.OnEventTracked -= OnEventTracked;

            EditorApplication.update -= OnEditorUpdate;

            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            rootVisualElement.Clear();

            _initialized = false;
        }

        private void OnDestroy()
        {
            _analyzer.RemoveTrackingListeners();
            SaveNodePositions();
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            // Reset session state
            SessionState.SetString("EventGraph_NodePositions", null);
            SessionState.SetString("EventGraph_LastAnalyzedGameObject", null);

            _lastAnalyzedGameObjects = null;
            ChangeRecordingStatus(false);
            ToggleEventInvocationListPanel(false);
            ResetEventLog();
            _graphView.ClearGraph();
            _graphData = null;

            LoadNodeGraphData();

            _analyzer = new EventGraphAnalyzer
            {
                SavedNodeGraphData = _nodeGraphData
            };
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                AnalyzeScene(_lastAnalyzedGameObjects);
            }
        }

        private void OnGUI()
        {
            if (!_initialized)
            {
                RestoreState();
            }
        }

        private void SaveState()
        {
            if (_lastAnalyzedGameObjects != null)
            {
                string[] gameObjectPaths = _lastAnalyzedGameObjects.Select(GetGameObjectFullPath).ToArray();
                string serializedPaths = string.Join(";", gameObjectPaths);
                SessionState.SetString("EventGraph_LastAnalyzedGameObjects", serializedPaths);
            }
            else
            {
                SessionState.SetString("EventGraph_LastAnalyzedGameObject", null);
            }

            SessionState.SetBool("EventGraph_IsReferenceSearchEnabled", _isReferenceSearchEnabled);

            SessionState.SetBool("EventGraph_IsPanelVisible", _isPanelVisible);
            SessionState.SetBool("EventGraph_IsRecording", _isRecording);

            if (_graphView != null)
            {
                SessionState.SetVector3("EventGraph_ViewPosition", _graphView.viewTransform.position);
                SessionState.SetVector3("EventGraph_ViewScale", _graphView.viewTransform.scale);

                var nodePositions = new SerializableDictionary<string, Vector2>();

                foreach (var node in _graphView.nodes.ToList())
                {
                    if (node is UnityEventNode unityNode)
                    {
                        string nodeId = GetGameObjectFullPath(unityNode.RepresentedObject);
                        Vector2 position = unityNode.GetPosition().position;
                        nodePositions[nodeId] = position;
                    }
                }

                string serializedPositions = JsonUtility.ToJson(nodePositions);
                SessionState.SetString("EventGraph_NodePositions", serializedPositions);
            }

            // Save filter settings
            SessionState.SetBool("EventGraph_InvokedNodesFilter", _filterManager.HasNodeFilter(GraphFilters.InvokedNodesFilter));
        }

        private void RestoreState()
        {
            string lastAnalyzedGameObjectPaths = SessionState.GetString("EventGraph_LastAnalyzedGameObjects", null);
            _lastAnalyzedGameObjects = null;
            if (!string.IsNullOrEmpty(lastAnalyzedGameObjectPaths))
            {
                string[] gameObjectPaths = lastAnalyzedGameObjectPaths.Split(';');
                _lastAnalyzedGameObjects = gameObjectPaths.Select(path => GameObject.Find(path)).ToArray();
            }

            _isReferenceSearchEnabled = SessionState.GetBool("EventGraph_IsReferenceSearchEnabled", false);
            _controlBar.SetReferenceSearch(_isReferenceSearchEnabled);

            _isPanelVisible = SessionState.GetBool("EventGraph_IsPanelVisible", false);
            ToggleEventInvocationListPanel(_isPanelVisible);

            _isRecording = SessionState.GetBool("EventGraph_IsRecording", false);
            ChangeRecordingStatus(_isRecording);

            Vector2 viewPosition = SessionState.GetVector3("EventGraph_ViewPosition", Vector2.zero);
            Vector3 viewScale = SessionState.GetVector3("EventGraph_ViewScale", Vector3.one);
            if (_graphView != null)
            {
                _graphView.viewTransform.position = viewPosition;
                _graphView.viewTransform.scale = viewScale;
            }

            if (_graphView != null)
            {
                string serializedPositions = SessionState.GetString("EventGraph_NodePositions", null);
                if (!string.IsNullOrEmpty(serializedPositions))
                {
                    var nodePositions = JsonUtility.FromJson<SerializableDictionary<string, Vector2>>(serializedPositions);
                    foreach (var node in _graphView.nodes.ToList())
                    {
                        if (node is UnityEventNode unityNode)
                        {
                            string nodeId = GetGameObjectFullPath(unityNode.RepresentedObject);
                            if (nodePositions.ContainsKey(nodeId))
                            {
                                unityNode.SetPosition(new Rect(nodePositions[nodeId], unityNode.GetPosition().size));
                            }
                        }
                    }
                }
            }

            bool invokedNodesFilterActive = SessionState.GetBool("EventGraph_InvokedNodesFilter", false);
            ToggleInvokedNodesFilter(invokedNodesFilterActive);
            _controlBar.SetShowInvokedNodesOnly(invokedNodesFilterActive);

            _initialized = true;
        }

        private void LoadNodeGraphData()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            string assetPath = $"Assets/EventGraphs/{sceneName}_EventGraphData.asset";

            _nodeGraphData = AssetDatabase.LoadAssetAtPath<NodeGraphData>(assetPath);
            if (_nodeGraphData == null)
            {
                _nodeGraphData = ScriptableObject.CreateInstance<NodeGraphData>();
                _nodeGraphData.sceneName = sceneName;

                // Ensure the directory exists
                string directoryPath = System.IO.Path.GetDirectoryName(assetPath);
                if (!System.IO.Directory.Exists(directoryPath))
                {
                    System.IO.Directory.CreateDirectory(directoryPath);
                }

                AssetDatabase.CreateAsset(_nodeGraphData, assetPath);
                AssetDatabase.SaveAssets();
            }
        }

        private void SaveNodePositions()
        {
            foreach (var node in _graphView.nodes.ToList())
            {
                if (node is UnityEventNode unityNode)
                {
                    string nodeId = GetGameObjectFullPath(unityNode.RepresentedObject);
                    Vector2 position = unityNode.GetPosition().position;
                    _nodeGraphData.SetNodePosition(nodeId, position);
                }
            }

            EditorUtility.SetDirty(_nodeGraphData);
            AssetDatabase.SaveAssets();
        }

        private string GetGameObjectFullPath(GameObject gameObject)
        {
            string path = gameObject.name;
            var parent = gameObject.transform.parent;
            while (parent != null)
            {
                path = $"{parent.gameObject.name}/{path}";
                parent = parent.parent;
            }
            return path;
        }

        private void AnalyzeFromSelectedGameObjects()
        {
            var selectedGameObjects = Selection.gameObjects;
            AnalyzeScene(selectedGameObjects);
        }

        private void AnalyzeScene(GameObject[] selectedGameObjects)
        {
            if (_isAnalyzing)
                return;
                
            _isAnalyzing = true;
            
            // For very large scenes, show progress bar and use background analysis
            if (selectedGameObjects == null || selectedGameObjects.Length == 0 || 
                CountAllChildrenRecursively(selectedGameObjects) > LARGE_GRAPH_THRESHOLD)
            {
                AnalyzeSceneProgressively(selectedGameObjects);
            }
            else
            {
                // Regular analysis for smaller graphs
                AnalyzeSceneImmediate(selectedGameObjects);
            }
        }
        
        private int CountAllChildrenRecursively(GameObject[] roots)
        {
            if (roots == null)
                return 0;
                
            int count = 0;
            foreach (var root in roots)
            {
                if (root == null) continue;
                count++; // Count the root
                count += root.GetComponentsInChildren<Transform>(true).Length - 1;
            }
            return count;
        }
        
        private void AnalyzeSceneProgressively(GameObject[] selectedGameObjects)
        {
            _graphView.ClearGraph();
            
            // Start the analysis in background
            EditorApplication.delayCall += () => {
                EditorUtility.DisplayProgressBar("Analyzing Scene", "Building event graph...", 0.1f);
                try
                {
                    _graphData = _analyzer.AnalyzeScene(selectedGameObjects, _isReferenceSearchEnabled);
                    _lastAnalyzedGameObjects = selectedGameObjects;
                    
                    // Start coroutine to populate graph progressively
                    EditorCoroutine.Start(PopulateGraphProgressively(_graphData.Nodes, _graphData.Edges));
                }
                finally
                {
                    _isAnalyzing = false;
                }
            };
        }
        
        private void AnalyzeSceneImmediate(GameObject[] selectedGameObjects)
        {
            _graphView.ClearGraph();

            _graphData = _analyzer.AnalyzeScene(selectedGameObjects, _isReferenceSearchEnabled);
            _lastAnalyzedGameObjects = selectedGameObjects;

            _graphView.PopulateGraph(_graphData.Nodes, _graphData.Edges);

            // Restore node positions
            if (_nodeGraphData != null)
            {
                foreach (var node in _graphData.Nodes)
                {
                    var representedObjectFullPath = GetGameObjectFullPath(node.RepresentedObject);
                    var position = _nodeGraphData.GetNodePosition(representedObjectFullPath);
                    if (position.HasValue)
                    {
                        node.SetPosition(new Rect(position.Value, node.GetPosition().size));
                    }
                }   
            }

            UpdateGraphViewFilters();
            _isAnalyzing = false;
        }
        
        private IEnumerator PopulateGraphProgressively(List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            const int batchSize = 50;
            int totalItems = nodes.Count + edges.Count;
            int processedItems = 0;
            List<UnityEventEdge> createdEdges = new List<UnityEventEdge>();
            
            try
            {
                // Add nodes in batches
                for (int i = 0; i < nodes.Count; i += batchSize)
                {
                    int endIndex = Mathf.Min(i + batchSize, nodes.Count);
                    
                    for (int j = i; j < endIndex; j++)
                    {
                        _graphView.AddElement(nodes[j]);
                        
                        // Restore node position if available
                        var representedObjectFullPath = GetGameObjectFullPath(nodes[j].RepresentedObject);
                        var position = _nodeGraphData?.GetNodePosition(representedObjectFullPath);
                        if (position.HasValue)
                        {
                            nodes[j].SetPosition(new Rect(position.Value, nodes[j].GetPosition().size));
                        }
                        
                        processedItems++;
                    }
                    
                    float progress = (float)processedItems / totalItems;
                    EditorUtility.DisplayProgressBar("Building Graph", $"Adding nodes ({processedItems}/{totalItems})...", progress);
                    yield return null;
                }
                
                // Add edges in batches
                for (int i = 0; i < edges.Count; i += batchSize)
                {
                    int endIndex = Mathf.Min(i + batchSize, edges.Count);
                    
                    for (int j = i; j < endIndex; j++)
                    {
                        var edge = UnityEventEdge.CreateEdge(edges[j]);
                        if (edge != null)
                        {
                            _graphView.AddElement(edge);
                            createdEdges.Add(edge);
                        }
                        
                        processedItems++;
                    }
                    
                    float progress = (float)processedItems / totalItems;
                    EditorUtility.DisplayProgressBar("Building Graph", $"Adding edges ({processedItems}/{totalItems})...", progress);
                    yield return null;
                }
                
                // After all nodes and edges are added, force update parameter positions
                EditorUtility.DisplayProgressBar("Building Graph", "Finalizing edge labels...", 1.0f);
                yield return null;
                
                // First update positions for visible edges
                foreach (var edge in createdEdges)
                {
                    if (edge.IsVisible())
                    {
                        edge.ForceUpdateParameterPosition();
                    }
                }
                
                yield return null;
                
                // Then update any remaining edges
                foreach (var edge in createdEdges)
                {
                    edge.ForceUpdateParameterPosition();
                }
                
                UpdateGraphViewFilters();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private EventData GetEventDataForNode(UnityEventNode node)
        {
            foreach (var unityEvent in node.GetUnityEvents())
            {
                var eventData = EventTracker.GetEventData(unityEvent);
                if (eventData != null && eventData.Invocations.Count > 0)
                {
                    return eventData.Invocations.Last();
                }
            }
            return null;
        }

        private void ToggleInvokedNodesFilter(bool isActive)
        {
            if (isActive)
            {
                _filterManager.AddNodeFilter(GraphFilters.InvokedNodesFilter);
            }
            else
            {
                _filterManager.RemoveNodeFilter(GraphFilters.InvokedNodesFilter);
            }

            UpdateGraphViewFilters();
        }

        public void UpdateGraphViewFilters()
        {
            _filterManager.ApplyFilters(_graphView);
        }

        private void ChangeRecordingStatus(bool? active = null)
        {
            if (active.HasValue)
            {
                _isRecording = active.Value;
            }
            else
            {
                _isRecording = !_isRecording;
            }

            _controlBar.SetRecording(_isRecording);
            if (_isRecording)
            {
                EventTracker.ClearInvokedEvents();
                _eventInvocationListPanel.ClearList();
            }
        }

        private void ClearHighlights()
        {
            foreach (var kvp in _highlightedNodes)
            {
                kvp.Key.ResetHighlight();
            }
            _highlightedNodes.Clear();

            foreach (var kvp in _highlightedEdges)
            {
                kvp.Key.edgeControl.inputColor = Color.white;
                kvp.Key.edgeControl.outputColor = Color.white;
            }
            _highlightedEdges.Clear();

            foreach (var kvp in _highlightedPorts)
            {
                kvp.Key.portColor = Color.white;
            }
            _highlightedPorts.Clear();
        }

        private void HighlightEventInGraph(EventData eventData, double duration = 1.0)
        {
            // Throttle highlights to avoid performance issues
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastHighlightTime < HIGHLIGHT_THROTTLE_INTERVAL && 
                duration < 1000) // Skip throttling for user-initiated highlights
            {
                return;
            }
            
            _lastHighlightTime = currentTime;
            
            var node = _graphData?.GetNodeForUnityEvent(eventData.unityEvent);
            if (node == null)
            {
                Debug.LogWarning("[EventGraph] Node for the event not found.");
                return;
            }

            if (!node.TryGetPortForUnityEvent(eventData.unityEvent, out var port))
            {
                Debug.LogWarning("[EventGraph] Port for the event not found.");
                return;
            }

            HighlightNode(node, duration);
            HighlightPort(port, duration);

            var edges = port.connections;
            
            // Limit the number of highlighted connections to prevent performance issues
            int maxHighlights = duration > 1000 ? 100 : 20; // Allow more for user-initiated highlights
            int highlightCount = 0;
            
            foreach (var edge in edges)
            {
                if (highlightCount++ >= maxHighlights)
                    break;
                    
                HighlightEdge(edge, duration);

                var targetNode = edge.input.node as UnityEventNode;
                if (targetNode != null)
                {
                    HighlightNode(targetNode, duration);

                    // Limit recursive highlighting depth for performance
                    if (duration < 1000 || highlightCount < 10)
                    {
                        var connectedEventData = GetEventDataForNode(targetNode);
                        if (connectedEventData != null)
                        {
                            HighlightEventInGraph(connectedEventData, duration);
                        }
                    }
                }
            }
        }

        private void OnEditorUpdate()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            var nodesToReset = new List<UnityEventNode>();
            var edgesToReset = new List<Edge>();
            var portsToReset = new List<UnityEventPort>();

            foreach (var kvp in _highlightedNodes)
            {
                if (currentTime >= kvp.Value)
                {
                    nodesToReset.Add(kvp.Key);
                }
            }

            foreach (var node in nodesToReset)
            {
                node.ResetHighlight();
                _highlightedNodes.Remove(node);
            }

            foreach (var kvp in _highlightedEdges)
            {
                if (currentTime >= kvp.Value)
                {
                    edgesToReset.Add(kvp.Key);
                }
            }

            foreach (var edge in edgesToReset)
            {
                edge.edgeControl.inputColor = Color.white;
                edge.edgeControl.outputColor = Color.white;
                _highlightedEdges.Remove(edge);
            }

            foreach (var kvp in _highlightedPorts)
            {
                if (currentTime >= kvp.Value)
                {
                    portsToReset.Add(kvp.Key);
                }
            }

            foreach (var port in portsToReset)
            {
                port.portColor = Color.white;
                _highlightedPorts.Remove(port);
            }
        }

        private void HighlightNode(UnityEventNode node, double duration = 1.0)
        {
            node.Highlight();
            double resetTime = EditorApplication.timeSinceStartup + duration;

            if (!_highlightedNodes.ContainsKey(node) || _highlightedNodes[node] < resetTime)
            {
                _highlightedNodes[node] = resetTime;
            }
        }

        private void HighlightEdge(Edge edge, double duration = 1.0)
        {
            edge.edgeControl.inputColor = Color.yellow;
            edge.edgeControl.outputColor = Color.yellow;
            double resetTime = EditorApplication.timeSinceStartup + duration;

            if (!_highlightedEdges.ContainsKey(edge) || _highlightedEdges[edge] < resetTime)
            {
                _highlightedEdges[edge] = resetTime;
            }
        }

        private void HighlightPort(UnityEventPort port, double duration = 1.0)
        {
            port.portColor = Color.yellow;
            double resetTime = EditorApplication.timeSinceStartup + duration;

            if (!_highlightedPorts.ContainsKey(port) || _highlightedPorts[port] < resetTime)
            {
                _highlightedPorts[port] = resetTime;
            }
        }

        private void ToggleEventInvocationListPanel(bool? visible = null)
        {
            if (visible.HasValue)
            {
                _isPanelVisible = visible.Value;
            }
            else
            {
                _isPanelVisible = !_isPanelVisible;
                ClearHighlights();
            }

            _eventInvocationListPanel.SetVisible(_isPanelVisible);
        }

        private void EventButtonClicked(EventData eventData, bool jumpToNode)
        {
            if (eventData.unityEvent != null)
            {
                var node = _graphData?.GetNodeForUnityEvent(eventData.unityEvent);
                if (node == null)
                {
                    Debug.LogWarning("[EventGraph] Node for the event not found.");
                    return;
                }

                if (jumpToNode)
                {
                    _graphView.ClearSelection();
                    _graphView.AddToSelection(node);
                    _graphView.FrameSelection();
                }

                ClearHighlights();
                HighlightEventInGraph(eventData, 9999);
            }
            else
            {
                Debug.LogWarning("[EventGraph] Node for the event not found.");
            }
        }

        private void OnEventTracked(EventData eventData)
        {
            var node = _graphData?.GetNodeForUnityEvent(eventData.unityEvent);
            if (node != null)
            {
                node.UpdateInvocationData();
            }

            if (_isRecording)
            {
                _eventInvocationListPanel.AddEvent(eventData);
            }
            
            HighlightEventInGraph(eventData);
            UpdateGraphViewFilters();
        }

        private void ResetInvocationCalls()
        {
            if (_graphData == null || _graphData.Nodes == null)
            {
                return;
            }
            foreach (var node in _graphData.Nodes)
            {
                node.UpdateInvocationData();
            }
        }

        private void ResetEventLog()
        {
            EventTracker.ClearInvokedEvents();
            _eventInvocationListPanel.ClearList();
            ClearHighlights();
            ResetInvocationCalls();
        }

        private void OnLayoutStrategyChanged(LayoutStrategyType strategyType)
        {
            ILayoutStrategy strategy = strategyType switch
            {
                LayoutStrategyType.ForceDirectedLayout => new ForceDirectedLayoutStrategy(),
                LayoutStrategyType.GridLayout => new GridLayoutStrategy(),
                LayoutStrategyType.RadialLayout => new RadialLayoutStrategy(),
                LayoutStrategyType.SharedEdgesClusterLayout => new SharedEdgesClusterLayoutStrategy(),
                _ => new ForceDirectedLayoutStrategy()
            };

            _graphView.SetLayoutStrategy(strategy);
            _graphView.AutoLayout();
        }
    }
    
    // Simple utility class to run a coroutine from editor scripts
    public class EditorCoroutine
    {
        private IEnumerator _routine;

        public static EditorCoroutine Start(IEnumerator routine)
        {
            EditorCoroutine coroutine = new EditorCoroutine(routine);
            coroutine.Start();
            return coroutine;
        }

        private EditorCoroutine(IEnumerator routine)
        {
            _routine = routine;
        }

        private void Start()
        {
            EditorApplication.update += Update;
        }

        private void Stop()
        {
            EditorApplication.update -= Update;
            _routine = null;
        }

        private void Update()
        {
            if (_routine.MoveNext() == false)
            {
                Stop();
            }
        }
    }
}
