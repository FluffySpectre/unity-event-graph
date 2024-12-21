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

        public static void TrackEvent(UnityEventBase unityEvent, string eventName, params object[] parameterValues)
        {
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

            OnEventTracked?.Invoke(eventDataObj);
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
