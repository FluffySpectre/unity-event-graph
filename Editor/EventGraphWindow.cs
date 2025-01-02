using System.Linq;
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

            // AnalyzeScene(_lastAnalyzedGameObjects);

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
                kvp.Key.contentContainer.Q<Label>().style.color = Color.white;
            }
            _highlightedPorts.Clear();
        }

        private void HighlightEventInGraph(EventData eventData, double duration = 1.0)
        {
            var node = _graphData.GetNodeForUnityEvent(eventData.unityEvent);
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
            foreach (var edge in edges)
            {
                HighlightEdge(edge, duration);

                HighlightPort(edge.input as UnityEventPort, duration);

                var targetNode = edge.input.node as UnityEventNode;
                if (targetNode != null)
                {
                    HighlightNode(targetNode, duration);

                    var connectedEventData = GetEventDataForNode(targetNode);
                    if (connectedEventData != null)
                    {
                        HighlightEventInGraph(connectedEventData, duration);
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
                port.contentContainer.Q<Label>().style.color = Color.white;
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
            port.contentContainer.Q<Label>().style.color = Color.yellow;

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
                var node = _graphData.GetNodeForUnityEvent(eventData.unityEvent);
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
            var node = _graphData.GetNodeForUnityEvent(eventData.unityEvent);
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
}
