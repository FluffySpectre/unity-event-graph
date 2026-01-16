using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace FluffySpectre.UnityEventGraph
{
    [Serializable]
    public class EdgeData
    {
        public UnityEventNode Source { get; private set; }
        public UnityEventNode Target { get; private set; }
        public string SourcePortName { get; private set; }
        public string TargetPortName { get; private set; }
        public string ParameterValue { get; private set; }

        public EdgeData(UnityEventNode source, UnityEventNode target, string sourcePortName, string targetPortName, string parameterValue = null)
        {
            Source = source;
            Target = target;
            SourcePortName = sourcePortName;
            TargetPortName = targetPortName;
            ParameterValue = parameterValue;
        }
    }

    [Serializable]
    public class EventGraphData
    {
        public List<UnityEventNode> Nodes { get; private set; }
        public List<EdgeData> Edges { get; private set; }
        public Dictionary<UnityEventBase, UnityEventNode> UnityEventNodeMapping { get; private set; }

        public EventGraphData(List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            Nodes = nodes;
            Edges = edges;
            UnityEventNodeMapping = new Dictionary<UnityEventBase, UnityEventNode>();

            foreach (var node in Nodes)
            {
                foreach (var unityEvent in node.GetUnityEvents())
                {
                    UnityEventNodeMapping[unityEvent] = node;
                }
            }
        }

        public UnityEventNode GetNodeForUnityEvent(UnityEventBase unityEvent)
        {
            UnityEventNodeMapping.TryGetValue(unityEvent, out var node);
            return node;
        }
    }

    public class EventGraphAnalyzer
    {
        public NodeGraphData SavedNodeGraphData { get; set; }

        private Dictionary<UnityEventBase, Delegate> _trackingDelegates = new();
        
        private static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new();
        private static readonly Dictionary<Type, MethodInfo[]> _methodCache = new();
        private static readonly Dictionary<Type, bool> _unityEventTypeCache = new();
        private static readonly Dictionary<Type, bool> _enumerableTypeCache = new();
        private static readonly FieldInfo _persistentCallsField;
        private static readonly Type _persistentCallGroupType;
        private static FieldInfo _callsField;
        private static readonly Dictionary<Type, FieldInfo> _targetFieldCache = new();
        private static readonly Dictionary<Type, FieldInfo> _methodFieldCache = new();
        private static readonly Dictionary<Type, FieldInfo> _argumentsFieldCache = new();
        private static readonly Dictionary<Type, ArgumentFieldSet> _argumentFieldSetCache = new();
        
        // Types to skip during analysis
        private static readonly HashSet<Type> _skipTypes = new()
        {
            typeof(Transform),
            typeof(string),
            typeof(Mesh),
            typeof(Material),
            typeof(Shader),
            typeof(Texture),
            typeof(Texture2D),
            typeof(Sprite),
            typeof(AnimationClip),
            typeof(AudioClip),
            typeof(Font),
        };

        private struct ArgumentFieldSet
        {
            public FieldInfo IntField;
            public FieldInfo FloatField;
            public FieldInfo StringField;
            public FieldInfo BoolField;
            public FieldInfo ObjectField;
        }

        static EventGraphAnalyzer()
        {
            _persistentCallsField = typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_persistentCallsField != null)
            {
                _persistentCallGroupType = _persistentCallsField.FieldType;
            }
        }

        public EventGraphData AnalyzeScene(GameObject[] selectedGameObjects = null, bool searchDirectReferencesOfSelectedComponents = false)
        {
            RemoveTrackingListeners();

            var nodes = new List<UnityEventNode>();
            var edges = new List<EdgeData>();
            
            // Use dictionary for O(1) node lookup instead of List.Find()
            var nodeMap = new Dictionary<GameObject, UnityEventNode>();

            if (selectedGameObjects != null && selectedGameObjects.Length > 0)
            {
                foreach (var root in selectedGameObjects)
                {
                    if (root == null) continue;
                    AnalyzeGameObject(root, nodes, edges, nodeMap);
                }

                if (searchDirectReferencesOfSelectedComponents)
                {
                    var selectedSet = new HashSet<GameObject>(selectedGameObjects);
                    FindReferencesToSelectedComponents(selectedSet, nodes, edges, nodeMap);
                }
            }
            else
            {
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    AnalyzeGameObject(root, nodes, edges, nodeMap);
                }
            }

            return new EventGraphData(nodes, edges);
        }

        private void FindReferencesToSelectedComponents(HashSet<GameObject> selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap)
        {
            var allGameObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var visitedGameObjects = new HashSet<GameObject>();

            foreach (var root in allGameObjects)
            {
                FindReferencesInGameObject(root, selectedGameObjects, nodes, edges, nodeMap, visitedGameObjects);
            }
        }

        private void FindReferencesInGameObject(GameObject gameObject, HashSet<GameObject> selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap, HashSet<GameObject> visitedGameObjects)
        {
            if (!visitedGameObjects.Add(gameObject))
                return;

            var components = gameObject.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;
                AnalyzeComponentForReferences(component, selectedGameObjects, nodes, edges, nodeMap);
            }

            var transform = gameObject.transform;
            int childCount = transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                FindReferencesInGameObject(transform.GetChild(i).gameObject, selectedGameObjects, nodes, edges, nodeMap, visitedGameObjects);
            }
        }

        private void AnalyzeComponentForReferences(Component component, HashSet<GameObject> selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap)
        {
            AnalyzeComponentFields(component, (comp, fieldName, unityEvent) =>
            {
                AnalyzeUnityEventForReferences(comp, fieldName, unityEvent, selectedGameObjects, nodes, edges, nodeMap);
            });
        }

        private bool AnalyzeComponent(Component component, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap)
        {
            bool hasUnityEvent = false;

            AnalyzeComponentFields(component, (comp, fieldName, unityEvent) =>
            {
                hasUnityEvent = true;
                string portName = $"{comp.gameObject.name}.{comp.GetType().Name}.{fieldName}";
                AnalyzeUnityEvent(comp, portName, unityEvent, nodes, edges, nodeMap);
            });

            return hasUnityEvent;
        }

        private void AnalyzeComponentFields(Component component, Action<Component, string, UnityEventBase> unityEventAction)
        {
            var type = component.GetType();
            
            // Skip known types that don't contain UnityEvents
            if (_skipTypes.Contains(type))
            {
                return;
            }
            
            var fields = GetCachedFields(type);
            var visited = HashSetPool<object>.Get();
            
            try
            {
                foreach (var field in fields)
                {
                    var fieldValue = field.GetValue(component);
                    if (fieldValue != null)
                    {
                        AnalyzeFieldValueRecursive(component, fieldValue, field.Name, unityEventAction, visited, 0);
                    }
                }
            }
            finally
            {
                HashSetPool<object>.Release(visited);
            }
        }

        private static FieldInfo[] GetCachedFields(Type type)
        {
            if (!_fieldCache.TryGetValue(type, out var fields))
            {
                fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _fieldCache[type] = fields;
            }
            return fields;
        }

        private static bool IsUnityEventType(Type type)
        {
            if (!_unityEventTypeCache.TryGetValue(type, out var result))
            {
                result = typeof(UnityEventBase).IsAssignableFrom(type);
                _unityEventTypeCache[type] = result;
            }
            return result;
        }

        private static bool IsEnumerableType(Type type)
        {
            if (!_enumerableTypeCache.TryGetValue(type, out var result))
            {
                result = typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string);
                _enumerableTypeCache[type] = result;
            }
            return result;
        }

        private void AnalyzeFieldValueRecursive(Component component, object fieldValue, string fieldName, Action<Component, string, UnityEventBase> unityEventAction, HashSet<object> visited, int depth)
        {
            // Use iterative approach to prevent stack overflow
            var stack = new Stack<(object value, string name, int depth)>();
            stack.Push((fieldValue, fieldName, depth));
            
            const int MaxDepth = 8;
            const int MaxIterations = 5000; // Safety limit
            int iterations = 0;
            
            while (stack.Count > 0 && iterations < MaxIterations)
            {
                iterations++;
                var (currentValue, currentName, currentDepth) = stack.Pop();
                
                if (currentDepth > MaxDepth || currentValue == null)
                {
                    continue;
                }
                    
                // Check if already visited (handles circular references)
                if (!visited.Add(currentValue))
                {
                    continue;
                }

                var fieldType = currentValue.GetType();

                if (IsUnityEventType(fieldType))
                {
                    var unityEvent = currentValue as UnityEventBase;
                    if (unityEvent != null && HasPersistentCalls(unityEvent))
                    {
                        unityEventAction(component, currentName, unityEvent);
                    }
                }
                else if (IsEnumerableType(fieldType))
                {
                    var enumerable = currentValue as IEnumerable;
                    if (enumerable != null)
                    {
                        int index = 0;
                        try
                        {
                            foreach (var item in enumerable)
                            {
                                if (item == null) continue;
                                stack.Push((item, $"{currentName}[{index}]", currentDepth + 1));
                                index++;
                                
                                if (index > 50) break;
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore enumeration errors
                        }
                    }
                }
                else if (fieldType.IsClass && !fieldType.IsPrimitive && !_skipTypes.Contains(fieldType))
                {
                    var nestedFields = GetCachedFields(fieldType);
                    foreach (var nestedField in nestedFields)
                    {
                        try
                        {
                            var nestedFieldValue = nestedField.GetValue(currentValue);
                            if (nestedFieldValue != null && !visited.Contains(nestedFieldValue))
                            {
                                stack.Push((nestedFieldValue, $"{currentName}.{nestedField.Name}", currentDepth + 1));
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore field access errors
                        }
                    }
                }
            }
        }

        private void AnalyzeUnityEvent(Component component, string sourcePortName, UnityEventBase unityEvent, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap)
        {
            var sourceNode = FindOrCreateNode(component.gameObject, nodes, nodeMap);
            AnalyzeUnityEventWithPredicate(component, sourceNode, sourcePortName, unityEvent, nodes, edges, nodeMap, target => true);
        }

        private void AnalyzeUnityEventForReferences(Component component, string eventName, UnityEventBase unityEvent, HashSet<GameObject> selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap)
        {
            var sourceNode = FindOrCreateNode(component.gameObject, nodes, nodeMap);
            string sourcePortName = $"{component.gameObject.name}.{component.GetType().Name}.{eventName}";

            AnalyzeUnityEventWithPredicate(component, sourceNode, sourcePortName, unityEvent, nodes, edges, nodeMap, target =>
            {
                if (target is Component targetComponent)
                    return selectedGameObjects.Contains(targetComponent.gameObject);
                else if (target is GameObject targetGameObject)
                    return selectedGameObjects.Contains(targetGameObject);
                return false;
            });
        }

        private void AnalyzeUnityEventWithPredicate(Component sourceComponent, UnityEventNode sourceNode, string sourcePortName, UnityEventBase unityEvent, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap, Func<UnityEngine.Object, bool> targetPredicate)
        {
            var calls = GetPersistentCalls(unityEvent);
            if (calls == null) return;

            bool hasValidTargets = false;

            foreach (var call in calls)
            {
                var target = GetCallTarget(call);
                var methodName = GetCallMethodName(call);
                var parameters = GetCallParameters(call, methodName, target);

                if (!targetPredicate(target))
                {
                    continue;
                }

                hasValidTargets = true;

                sourceNode.AddOutputPort(sourcePortName, unityEvent);

                UnityEventNode targetNode = null;
                string targetPortName = null;

                if (target is Component targetComponent)
                {
                    targetNode = FindOrCreateNode(targetComponent.gameObject, nodes, nodeMap);
                    targetPortName = $"{targetComponent.gameObject.name}.{targetComponent.GetType().Name}.{methodName}";
                    targetNode.AddInputPort(targetPortName, targetComponent);
                }
                else if (target is GameObject targetGameObject)
                {
                    targetNode = FindOrCreateNode(targetGameObject, nodes, nodeMap);
                    targetPortName = $"{targetGameObject.name}.{methodName}";
                    targetNode.AddInputPort(targetPortName);
                }

                if (targetNode != null)
                {
                    var parametersString = parameters != null
                        ? string.Join("\n", parameters.Select(p => p?.ToString() ?? "null"))
                        : null;

                    var edgeData = new EdgeData(sourceNode, targetNode, sourcePortName, targetPortName, parametersString);
                    edges.Add(edgeData);
                }
            }

            if (hasValidTargets)
            {
                AddTrackingListener(unityEvent, sourcePortName);
            }
            else if (!sourceNode.HasPorts())
            {
                nodes.Remove(sourceNode);
                nodeMap.Remove(sourceNode.RepresentedObject);
            }
        }

        private void AnalyzeGameObject(GameObject gameObject, List<UnityEventNode> nodes, List<EdgeData> edges, Dictionary<GameObject, UnityEventNode> nodeMap)
        {
            bool hasUnityEvents = false;

            var components = gameObject.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;
                hasUnityEvents |= AnalyzeComponent(component, nodes, edges, nodeMap);
            }

            var transform = gameObject.transform;
            int childCount = transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                AnalyzeGameObject(transform.GetChild(i).gameObject, nodes, edges, nodeMap);
            }

            if (hasUnityEvents)
            {
                var node = FindOrCreateNode(gameObject, nodes, nodeMap);
                if (!node.HasPorts())
                {
                    nodes.Remove(node);
                    nodeMap.Remove(gameObject);
                }
            }
        }

        private bool HasPersistentCalls(UnityEventBase unityEvent)
        {
            return unityEvent.GetPersistentEventCount() > 0;
        }

        private IList GetPersistentCalls(UnityEventBase unityEvent)
        {
            if (_persistentCallsField == null) return null;

            var persistentCalls = _persistentCallsField.GetValue(unityEvent);
            if (persistentCalls == null) return null;

            if (_callsField == null)
            {
                _callsField = persistentCalls.GetType().GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            
            return _callsField?.GetValue(persistentCalls) as IList;
        }

        private UnityEngine.Object GetCallTarget(object call)
        {
            var type = call.GetType();
            if (!_targetFieldCache.TryGetValue(type, out var targetField))
            {
                targetField = type.GetField("m_Target", BindingFlags.Instance | BindingFlags.NonPublic);
                _targetFieldCache[type] = targetField;
            }
            return targetField?.GetValue(call) as UnityEngine.Object;
        }

        private string GetCallMethodName(object call)
        {
            var type = call.GetType();
            if (!_methodFieldCache.TryGetValue(type, out var methodField))
            {
                methodField = type.GetField("m_MethodName", BindingFlags.Instance | BindingFlags.NonPublic);
                _methodFieldCache[type] = methodField;
            }
            return methodField?.GetValue(call) as string;
        }

        private object[] GetCallParameters(object call, string methodName, UnityEngine.Object target)
        {
            if (string.IsNullOrEmpty(methodName) || target == null)
                return null;

            var callType = call.GetType();
            if (!_argumentsFieldCache.TryGetValue(callType, out var argumentsField))
            {
                argumentsField = callType.GetField("m_Arguments", BindingFlags.Instance | BindingFlags.NonPublic);
                _argumentsFieldCache[callType] = argumentsField;
            }
            
            var arguments = argumentsField?.GetValue(call);
            if (arguments == null) return null;

            var argType = arguments.GetType();
            if (!_argumentFieldSetCache.TryGetValue(argType, out var argFields))
            {
                argFields = new ArgumentFieldSet
                {
                    IntField = argType.GetField("m_IntArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                    FloatField = argType.GetField("m_FloatArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                    StringField = argType.GetField("m_StringArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                    BoolField = argType.GetField("m_BoolArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                    ObjectField = argType.GetField("m_ObjectArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                };
                _argumentFieldSetCache[argType] = argFields;
            }

            int intArg = argFields.IntField != null ? (int)argFields.IntField.GetValue(arguments) : default;
            float floatArg = argFields.FloatField != null ? (float)argFields.FloatField.GetValue(arguments) : default;
            string stringArg = argFields.StringField != null ? (string)argFields.StringField.GetValue(arguments) : null;
            bool boolArg = argFields.BoolField != null ? (bool)argFields.BoolField.GetValue(arguments) : default;
            UnityEngine.Object objectArg = argFields.ObjectField != null ? (UnityEngine.Object)argFields.ObjectField.GetValue(arguments) : null;

            var targetType = target.GetType();
            var method = FindMatchingMethodWithParameters(targetType, methodName, intArg, floatArg, stringArg, boolArg, objectArg);

            if (method == null)
                return null;

            var candidateParameters = method.GetParameters();
            if (candidateParameters.Length == 0)
                return null;

            var parameterValues = new object[candidateParameters.Length];
            for (int i = 0; i < candidateParameters.Length; i++)
            {
                var pType = candidateParameters[i].ParameterType;
                parameterValues[i] = GetArgumentForParameter(pType, intArg, floatArg, stringArg, boolArg, objectArg);
            }

            return parameterValues;
        }

        private MethodInfo FindMatchingMethodWithParameters(Type targetType, string methodName, int intArg, float floatArg, string stringArg, bool boolArg, UnityEngine.Object objectArg)
        {
            if (!_methodCache.TryGetValue(targetType, out var allMethods))
            {
                allMethods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _methodCache[targetType] = allMethods;
            }

            MethodInfo firstMatch = null;
            foreach (var method in allMethods)
            {
                if (method.Name == methodName)
                {
                    if (firstMatch == null)
                        firstMatch = method;
                    else
                    {
                        // Multiple methods found, check parameters
                        var candidateParameters = method.GetParameters();
                        if (ParametersCouldMatch(candidateParameters, intArg, floatArg, stringArg, boolArg, objectArg))
                            return method;
                    }
                }
            }

            return firstMatch;
        }

        private bool ParametersCouldMatch(ParameterInfo[] candidateParameters, int intArg, float floatArg, string stringArg, bool boolArg, UnityEngine.Object objectArg)
        {
            return true;
        }

        private object GetArgumentForParameter(Type pType, int intArg, float floatArg, string stringArg, bool boolArg, UnityEngine.Object objectArg)
        {
            if (pType == typeof(int)) return intArg;
            if (pType == typeof(float)) return floatArg;
            if (pType == typeof(string)) return stringArg;
            if (pType == typeof(bool)) return boolArg;
            if (typeof(UnityEngine.Object).IsAssignableFrom(pType)) return objectArg;
            return null;
        }

        private void AddTrackingListener(UnityEventBase unityEvent, string eventName)
        {
            if (_trackingDelegates.ContainsKey(unityEvent))
                return;

            var eventType = unityEvent.GetType();
            var invokeMethod = eventType.GetMethod("Invoke");
            var parameters = invokeMethod.GetParameters();

            MethodInfo addListenerMethod = eventType.GetMethod("AddListener");

            if (addListenerMethod != null)
            {
                var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();
                var actionType = GetUnityActionType(parameterTypes);

                if (actionType != null)
                {
                    var trackingDelegate = CreateTrackingDelegate(actionType, unityEvent, eventName);
                    addListenerMethod.Invoke(unityEvent, new object[] { trackingDelegate });
                    _trackingDelegates[unityEvent] = trackingDelegate;
                }
            }
        }

        private Type GetUnityActionType(Type[] parameterTypes)
        {
            return parameterTypes.Length switch
            {
                0 => typeof(UnityAction),
                1 => typeof(UnityAction<>).MakeGenericType(parameterTypes),
                2 => typeof(UnityAction<,>).MakeGenericType(parameterTypes),
                3 => typeof(UnityAction<,,>).MakeGenericType(parameterTypes),
                4 => typeof(UnityAction<,,,>).MakeGenericType(parameterTypes),
                _ => null
            };
        }

        private Delegate CreateTrackingDelegate(Type actionType, UnityEventBase unityEvent, string eventName)
        {
            var invokeMethod = actionType.GetMethod("Invoke");

            var parameterExpressions = invokeMethod
                .GetParameters()
                .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                .ToArray();

            var convertedParameters = parameterExpressions
                .Select(p => Expression.Convert(p, typeof(object)))
                .ToArray();

            var parameterArrayExpression = Expression.NewArrayInit(typeof(object), convertedParameters);

            var trackEventCall = Expression.Call(
                typeof(EventTracker),
                nameof(EventTracker.TrackEvent),
                null,
                Expression.Constant(unityEvent),
                Expression.Constant(eventName),
                parameterArrayExpression
            );

            var lambdaBody = Expression.Block(trackEventCall);
            var lambda = Expression.Lambda(actionType, lambdaBody, parameterExpressions);
            return lambda.Compile();
        }

        public void RemoveTrackingListeners()
        {
            foreach (var kvp in _trackingDelegates)
            {
                var unityEvent = kvp.Key;
                var trackingDelegate = kvp.Value;

                var removeListenerMethod = unityEvent.GetType().GetMethod("RemoveListener");
                removeListenerMethod?.Invoke(unityEvent, new object[] { trackingDelegate });
            }
            _trackingDelegates.Clear();
        }

        private UnityEventNode FindOrCreateNode(GameObject gameObject, List<UnityEventNode> nodes, Dictionary<GameObject, UnityEventNode> nodeMap)
        {
            if (nodeMap.TryGetValue(gameObject, out var existingNode))
                return existingNode;

            var node = new UnityEventNode(gameObject.name, gameObject);
            nodes.Add(node);
            nodeMap[gameObject] = node;
            return node;
        }
    }

    internal static class HashSetPool<T>
    {
        private static readonly Stack<HashSet<T>> _pool = new();

        public static HashSet<T> Get()
        {
            if (_pool.Count > 0)
            {
                var set = _pool.Pop();
                set.Clear();
                return set;
            }
            return new HashSet<T>();
        }

        public static void Release(HashSet<T> set)
        {
            set.Clear();
            _pool.Push(set);
        }
    }
}
