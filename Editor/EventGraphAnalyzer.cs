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

            // Initialize mapping based on nodes and their UnityEvents
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

        public EventGraphData AnalyzeScene(GameObject[] selectedGameObjects = null, bool searchDirectReferencesOfSelectedComponents = false)
        {
            RemoveTrackingListeners();

            var nodes = new List<UnityEventNode>();
            var edges = new List<EdgeData>();

            if (selectedGameObjects != null && selectedGameObjects.Length > 0)
            {
                // Analyze the selected GameObjects
                foreach (var root in selectedGameObjects)
                {
                    if (root == null)
                    {
                        continue;
                    }
                    AnalyzeGameObject(root, nodes, edges);
                }

                if (searchDirectReferencesOfSelectedComponents)
                {
                    FindReferencesToSelectedComponents(selectedGameObjects, nodes, edges);
                }
            }
            else
            {
                // Analyze the current scene
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    AnalyzeGameObject(root, nodes, edges);
                }
            }

            return new EventGraphData(nodes, edges);
        }

        private void FindReferencesToSelectedComponents(GameObject[] selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            var allGameObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var visitedGameObjects = new HashSet<GameObject>();

            foreach (var root in allGameObjects)
            {
                FindReferencesInGameObject(root, selectedGameObjects, nodes, edges, visitedGameObjects);
            }
        }

        private void FindReferencesInGameObject(GameObject gameObject, GameObject[] selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges, HashSet<GameObject> visitedGameObjects)
        {
            if (visitedGameObjects.Contains(gameObject))
                return;

            visitedGameObjects.Add(gameObject);

            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null) continue;

                AnalyzeComponentForReferences(component, selectedGameObjects, nodes, edges);
            }

            foreach (Transform child in gameObject.transform)
            {
                FindReferencesInGameObject(child.gameObject, selectedGameObjects, nodes, edges, visitedGameObjects);
            }
        }

        private void AnalyzeComponentForReferences(Component component, GameObject[] selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            AnalyzeComponentFields(component, (comp, fieldName, unityEvent) =>
            {
                AnalyzeUnityEventForReferences(comp, fieldName, unityEvent, selectedGameObjects, nodes, edges);
            });
        }

        private bool AnalyzeComponent(Component component, List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            bool hasUnityEvent = false;

            AnalyzeComponentFields(component, (comp, fieldName, unityEvent) =>
            {
                hasUnityEvent = true;
                string portName = $"{comp.gameObject.name}.{comp.GetType().Name}.{fieldName}";

                AnalyzeUnityEvent(comp, portName, unityEvent, nodes, edges);
            });

            return hasUnityEvent;
        }

        private void AnalyzeComponentFields(Component component, Action<Component, string, UnityEventBase> unityEventAction)
        {
            var type = component.GetType();
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                var fieldValue = field.GetValue(component);
                if (fieldValue != null)
                {
                    AnalyzeFieldValueRecursive(component, fieldValue, field.Name, unityEventAction, new HashSet<object>());
                }
            }
        }

        private void AnalyzeFieldValueRecursive(Component component, object fieldValue, string fieldName, Action<Component, string, UnityEventBase> unityEventAction, HashSet<object> visited)
        {
            if (fieldValue == null || visited.Contains(fieldValue))
            {
                return;
            }

            visited.Add(fieldValue);

            var fieldType = fieldValue.GetType();

            if (typeof(UnityEventBase).IsAssignableFrom(fieldType))
            {
                var unityEvent = fieldValue as UnityEventBase;
                if (unityEvent != null && HasPersistentCalls(unityEvent))
                {
                    unityEventAction(component, fieldName, unityEvent);
                }
            }
            else if (typeof(IEnumerable).IsAssignableFrom(fieldType) && fieldType != typeof(string))
            {
                var enumerable = fieldValue as IEnumerable;
                if (enumerable != null)
                {
                    int index = 0;
                    try
                    {
                        foreach (var item in enumerable)
                        {
                            if (item == null) continue;
                            AnalyzeFieldValueRecursive(component, item, $"{fieldName}[{index}]", unityEventAction, visited);
                            index++;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore
                    }
                }
            }
            else if (fieldType.IsClass && fieldType != typeof(string) && !fieldType.IsPrimitive)
            {
                var nestedFields = fieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var nestedField in nestedFields)
                {
                    var nestedFieldValue = nestedField.GetValue(fieldValue);
                    AnalyzeFieldValueRecursive(component, nestedFieldValue, $"{fieldName}.{nestedField.Name}", unityEventAction, visited);
                }
            }
        }

        private void AnalyzeUnityEvent(Component component, string sourcePortName, UnityEventBase unityEvent, List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            var sourceNode = FindOrCreateNode(component.gameObject, nodes);

            AnalyzeUnityEventWithPredicate(component, sourceNode, sourcePortName, unityEvent, nodes, edges, target => true);
        }

        private void AnalyzeUnityEventForReferences(Component component, string eventName, UnityEventBase unityEvent, GameObject[] selectedGameObjects, List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            var sourceNode = FindOrCreateNode(component.gameObject, nodes);
            string sourcePortName = $"{component.gameObject.name}.{component.GetType().Name}.{eventName}";

            AnalyzeUnityEventWithPredicate(component, sourceNode, sourcePortName, unityEvent, nodes, edges, target =>
            {
                if (target is Component targetComponent)
                {
                    return selectedGameObjects.Contains(targetComponent.gameObject);
                }
                else if (target is GameObject targetGameObject)
                {
                    return selectedGameObjects.Contains(targetGameObject);
                }
                else
                {
                    return false;
                }
            });
        }

        private void AnalyzeUnityEventWithPredicate(Component sourceComponent, UnityEventNode sourceNode, string sourcePortName, UnityEventBase unityEvent, List<UnityEventNode> nodes, List<EdgeData> edges, Func<UnityEngine.Object, bool> targetPredicate)
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
                    targetNode = FindOrCreateNode(targetComponent.gameObject, nodes);
                    targetPortName = $"{targetComponent.gameObject.name}.{targetComponent.GetType().Name}.{methodName}";
                    targetNode.AddInputPort(targetPortName, targetComponent);
                }
                else if (target is GameObject targetGameObject)
                {
                    targetNode = FindOrCreateNode(targetGameObject, nodes);
                    targetPortName = $"{targetGameObject.name}.{methodName}";
                    targetNode.AddInputPort(targetPortName);
                }

                if (targetNode != null)
                {
                    // Format the parameters as a string
                    var parametersString = parameters != null 
                        ? string.Join("\n", parameters.Select(p => p?.ToString() ?? "null")) 
                        : null;

                    var edgeData = new EdgeData(sourceNode, targetNode, sourcePortName, targetPortName, parametersString);
                    edges.Add(edgeData);
                }
            }

            if (hasValidTargets)
            {
                // TODO: Move this into a separate class
                AddTrackingListener(unityEvent, sourcePortName);
            }
            else
            {
                // Remove the node if it has no valid targets
                if (!sourceNode.HasPorts())
                {
                    nodes.Remove(sourceNode);
                }
            }
        }

        private void AnalyzeGameObject(GameObject gameObject, List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            bool hasUnityEvents = false;

            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null) continue;
                hasUnityEvents |= AnalyzeComponent(component, nodes, edges);
            }

            foreach (Transform child in gameObject.transform)
            {
                AnalyzeGameObject(child.gameObject, nodes, edges);
            }

            // Remove the node if it has no UnityEvents
            if (hasUnityEvents)
            {
                var node = FindOrCreateNode(gameObject, nodes);
                if (!node.HasPorts())
                {
                    nodes.Remove(node);
                }
            }
        }

        private bool HasPersistentCalls(UnityEventBase unityEvent)
        {
            int count = unityEvent.GetPersistentEventCount();
            return count > 0;
        }

        private IList GetPersistentCalls(UnityEventBase unityEvent)
        {
            var persistentCallsField = typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic);
            if (persistentCallsField == null) return null;

            var persistentCalls = persistentCallsField.GetValue(unityEvent);
            if (persistentCalls == null) return null;

            var callsField = persistentCalls.GetType().GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
            if (callsField == null) return null;

            return callsField.GetValue(persistentCalls) as IList;
        }

        private UnityEngine.Object GetCallTarget(object call)
        {
            var targetField = call.GetType().GetField("m_Target", BindingFlags.Instance | BindingFlags.NonPublic);
            return targetField?.GetValue(call) as UnityEngine.Object;
        }

        private string GetCallMethodName(object call)
        {
            var methodField = call.GetType().GetField("m_MethodName", BindingFlags.Instance | BindingFlags.NonPublic);
            return methodField?.GetValue(call) as string;
        }

        private object[] GetCallParameters(object call, string methodName, UnityEngine.Object target)
        {
            if (string.IsNullOrEmpty(methodName) || target == null)
            {
                return null;
            }

            var targetType = target.GetType();
            var argumentsField = call.GetType().GetField("m_Arguments", BindingFlags.Instance | BindingFlags.NonPublic);
            var arguments = argumentsField?.GetValue(call);

            // Get all possible argument types
            var intField = arguments?.GetType().GetField("m_IntArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var floatField = arguments?.GetType().GetField("m_FloatArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var stringField = arguments?.GetType().GetField("m_StringArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var boolField = arguments?.GetType().GetField("m_BoolArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var objectField = arguments?.GetType().GetField("m_ObjectArgument", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            int intArg = intField != null ? (int)intField.GetValue(arguments) : default;
            float floatArg = floatField != null ? (float)floatField.GetValue(arguments) : default;
            string stringArg = stringField != null ? (string)stringField.GetValue(arguments) : null;
            bool boolArg = boolField != null ? (bool)boolField.GetValue(arguments) : default;
            UnityEngine.Object objectArg = objectField != null ? (UnityEngine.Object)objectField.GetValue(arguments) : null;

            // Find a method that matches the parameters
            var method = FindMatchingMethodWithParameters(targetType, methodName, intArg, floatArg, stringArg, boolArg, objectArg);

            if (method == null)
            {
                return null;
            }

            // Get parameters of the method
            var candidateParameters = method.GetParameters();
            if (candidateParameters.Length == 0)
            {
                return null;
            }

            // Assign the arguments to the parameters
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
            var methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == methodName)
                .ToArray();

            if (methods.Length == 0)
            {
                return null;
            }

            if (methods.Length == 1)
            {
                return methods[0];
            }

            // Find a method that matches the parameters
            foreach (var candidate in methods)
            {
                var candidateParameters = candidate.GetParameters();
                if (ParametersCouldMatch(candidateParameters, intArg, floatArg, stringArg, boolArg, objectArg))
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool ParametersCouldMatch(ParameterInfo[] candidateParameters, int intArg, float floatArg, string stringArg, bool boolArg, UnityEngine.Object objectArg)
        {
            // TODO: Maybe add more sophisticated parameter matching
            return true;
        }

        private object GetArgumentForParameter(Type pType, int intArg, float floatArg, string stringArg, bool boolArg, UnityEngine.Object objectArg)
        {
            if (pType == typeof(int))
            {
                return intArg; // Default = 0
            }
            else if (pType == typeof(float))
            {
                return floatArg; // Default = 0f
            }
            else if (pType == typeof(string))
            {
                return stringArg; // Default = null
            }
            else if (pType == typeof(bool))
            {
                return boolArg; // Default = false
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(pType))
            {
                return objectArg; // Default = null
            }
            else
            {
                // Type not supported
                return null;
            }
        }

        private void AddTrackingListener(UnityEventBase unityEvent, string eventName)
        {
            if (_trackingDelegates.ContainsKey(unityEvent))
            {
                return;
            }

            var eventType = unityEvent.GetType();
            var invokeMethod = eventType.GetMethod("Invoke");
            var parameters = invokeMethod.GetParameters();

            MethodInfo addListenerMethod = eventType.GetMethod("AddListener");

            if (addListenerMethod != null)
            {
                Delegate trackingDelegate = null;

                var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();

                var actionType = GetUnityActionType(parameterTypes);

                if (actionType != null)
                {
                    trackingDelegate = CreateTrackingDelegate(actionType, unityEvent, eventName);

                    addListenerMethod.Invoke(unityEvent, new object[] { trackingDelegate });

                    _trackingDelegates[unityEvent] = trackingDelegate;
                }
                else
                {
                    Debug.LogWarning("[EventGraph] Event-Tracker: Unsupported number of parameters in UnityEvent.");
                }
            }
        }

        private Type GetUnityActionType(Type[] parameterTypes)
        {
            switch (parameterTypes.Length)
            {
                case 0:
                    return typeof(UnityAction);
                case 1:
                    return typeof(UnityAction<>).MakeGenericType(parameterTypes);
                case 2:
                    return typeof(UnityAction<,>).MakeGenericType(parameterTypes);
                case 3:
                    return typeof(UnityAction<,,>).MakeGenericType(parameterTypes);
                case 4:
                    return typeof(UnityAction<,,,>).MakeGenericType(parameterTypes);
                default:
                    return null;
            }
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
                if (removeListenerMethod != null)
                {
                    removeListenerMethod.Invoke(unityEvent, new object[] { trackingDelegate });
                }
            }
            _trackingDelegates.Clear();
        }

        private UnityEventNode FindOrCreateNode(GameObject gameObject, List<UnityEventNode> nodes)
        {
            var node = nodes.Find(n => n.RepresentedObject == gameObject);
            if (node == null)
            {
                node = new UnityEventNode(gameObject.name, gameObject);
                nodes.Add(node);
            }
            return node;
        }
    }
}
