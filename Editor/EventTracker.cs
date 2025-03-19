using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace FluffySpectre.UnityEventGraph
{
    public static class EventTracker
    {
        public static Action<EventData> OnEventTracked { get; set; }

        private static Dictionary<UnityEventBase, EventInvocationData> _eventData = new();
        private static int _globalInvocationOrder = 0;
        private static int _currentTrackingDepth = 0;
        private static double _lastProcessedTime = 0;
        
        // Configuration
        private const int MAX_TRACKING_DEPTH = 50;
        private const double THROTTLE_INTERVAL = 0.05; // Seconds

        public static void TrackEvent(UnityEventBase unityEvent, string eventName, params object[] parameterValues)
        {
            _currentTrackingDepth++;
            
            try
            {
                // Prevent stack overflow with max depth check
                if (_currentTrackingDepth > MAX_TRACKING_DEPTH)
                {
                    Debug.LogWarning($"[EventGraph] Maximum tracking depth of {MAX_TRACKING_DEPTH} reached, possibly due to circular event references.");
                    return;
                }
                
                // Throttle processing for high-frequency events
                double currentTime = Time.realtimeSinceStartup;
                if (currentTime - _lastProcessedTime < THROTTLE_INTERVAL && _currentTrackingDepth > 1)
                {
                    // Skip tracking this event to reduce overhead
                    return;
                }
                
                _lastProcessedTime = currentTime;

                if (!_eventData.ContainsKey(unityEvent))
                {
                    _eventData[unityEvent] = new EventInvocationData();
                }

                var data = _eventData[unityEvent];
                data.InvocationCount++;
                data.LastInvocationTime = Time.time;

                _globalInvocationOrder++;

                var eventDataObj = new EventData
                {
                    name = eventName,
                    invocationOrder = _globalInvocationOrder,
                    unityEvent = unityEvent,
                    parameterValues = parameterValues
                };
                data.Invocations.Add(eventDataObj);

                // Only trigger the event if we're at the top level to prevent overflows
                if (_currentTrackingDepth == 1)
                {
                    OnEventTracked?.Invoke(eventDataObj);
                }
            }
            finally
            {
                _currentTrackingDepth--;
            }
        }

        public static EventInvocationData GetEventData(UnityEventBase unityEvent)
        {
            if (_eventData.TryGetValue(unityEvent, out var data))
            {
                return data;
            }
            return null;
        }

        public static List<EventData> GetAllInvokedEvents()
        {
            return _eventData.Values
                .SelectMany(d => d.Invocations)
                .OrderBy(d => d.invocationOrder)
                .ToList();
        }

        public static void ClearInvokedEvents()
        {
            _eventData.Clear();
            _globalInvocationOrder = 0;
        }
    }

    public class EventInvocationData
    {
        public int InvocationCount { get; set; }
        public float LastInvocationTime { get; set; }
        public List<EventData> Invocations { get; set; } = new List<EventData>();
    }

    [Serializable]
    public class EventData
    {
        public string name;
        public int invocationOrder;
        public UnityEventBase unityEvent;
        public object[] parameterValues;
    }
}
