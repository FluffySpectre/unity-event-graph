using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace FluffySpectre.UnityEventGraph
{
    public static class EventTracker
    {
        public static Action<EventData> OnEventTracked { get; set; }

        private static readonly Dictionary<UnityEventBase, EventInvocationData> _eventData = new();
        private static readonly List<EventData> _allInvokedEventsCache = new();
        private static int _globalInvocationOrder = 0;
        private static bool _cacheValid = false;

        public static void TrackEvent(UnityEventBase unityEvent, string eventName, params object[] parameterValues)
        {
            if (!_eventData.TryGetValue(unityEvent, out var data))
            {
                data = new EventInvocationData();
                _eventData[unityEvent] = data;
            }

            data.InvocationCount++;
            data.LastInvocationTime = Time.time;

            _globalInvocationOrder++;
            _cacheValid = false;

            var eventDataObj = new EventData
            {
                name = eventName,
                invocationOrder = _globalInvocationOrder,
                unityEvent = unityEvent,
                parameterValues = parameterValues
            };
            data.Invocations.Add(eventDataObj);

            OnEventTracked?.Invoke(eventDataObj);
        }

        public static EventInvocationData GetEventData(UnityEventBase unityEvent)
        {
            _eventData.TryGetValue(unityEvent, out var data);
            return data;
        }

        public static List<EventData> GetAllInvokedEvents()
        {
            if (_cacheValid)
            {
                return _allInvokedEventsCache;
            }

            _allInvokedEventsCache.Clear();
            
            foreach (var data in _eventData.Values)
            {
                _allInvokedEventsCache.AddRange(data.Invocations);
            }
            
            _allInvokedEventsCache.Sort((a, b) => a.invocationOrder.CompareTo(b.invocationOrder));
            _cacheValid = true;
            
            return _allInvokedEventsCache;
        }

        public static void ClearInvokedEvents()
        {
            _eventData.Clear();
            _allInvokedEventsCache.Clear();
            _globalInvocationOrder = 0;
            _cacheValid = false;
        }
    }

    public class EventInvocationData
    {
        public int InvocationCount { get; set; }
        public float LastInvocationTime { get; set; }
        public List<EventData> Invocations { get; } = new List<EventData>();
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
