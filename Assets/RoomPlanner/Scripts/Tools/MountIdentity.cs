using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Tools
{
    /// <summary>Stable identity for a construction surface, independent of scene registry order.</summary>
    public sealed class MountIdentity : MonoBehaviour
    {
        private static readonly Dictionary<string, MountIdentity> Registry = new();
        [SerializeField] private string key;
        public string Key => key;

        public static string GetOrCreate(GameObject host)
        {
            if (host == null) return null;
            var wall=host.GetComponentInParent<RoomPlanner.Walls.Wall>();
            var floor=host.GetComponentInParent<RoomPlanner.Floors.Floor>();
            var root=wall!=null?wall.gameObject:floor!=null?floor.gameObject:host;
            var identity=root.GetComponent<MountIdentity>();
            if(identity==null)identity=root.AddComponent<MountIdentity>();
            if(string.IsNullOrEmpty(identity.key))identity.Assign(Guid.NewGuid().ToString("N"));
            return identity.key;
        }
        public static string Existing(GameObject host)=>host!=null?host.GetComponent<MountIdentity>()?.Key:null;
        public static GameObject Find(string id)=>!string.IsNullOrEmpty(id)&&Registry.TryGetValue(id,out var value)&&value!=null?value.gameObject:null;
        public static void Restore(GameObject host,string id)
        {
            if(host==null||string.IsNullOrEmpty(id))return;
            var identity=host.GetComponent<MountIdentity>();
            if(identity==null)identity=host.AddComponent<MountIdentity>();
            identity.Assign(id);
        }
        private void Assign(string id)
        {
            Remove();key=id;
            Registry[key]=this;
        }
        private void Awake(){if(!string.IsNullOrEmpty(key))Registry[key]=this;}
        private void Remove(){if(!string.IsNullOrEmpty(key)&&Registry.TryGetValue(key,out var old)&&old==this)Registry.Remove(key);}
        private void OnDestroy()=>Remove();
    }
}
