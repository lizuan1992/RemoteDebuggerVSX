// This file implements protocol handling according to RemoteProtocol.md documentation.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace RemoteAD7Engine
{
    internal sealed class ProtocolVariable
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }

        // Protocol fields
        public long Addr { get; set; }
        public int TypeId { get; set; }
        public int Size { get; set; }
        public List<long> Elements { get; set; }

        internal static long ParseAndStoreVariable(JObject obj, string command)
        {
            // Validate and read required fields
            if (!obj.TryGetValue("name", out var nameToken) || nameToken == null)
            {
                Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: missing name");
                return 0; // Invalid: missing name
            }
            string name = (string)nameToken;

            if (!obj.TryGetValue("value", out var valueToken) || valueToken == null)
            {
                Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: missing value");
                return 0; // Invalid: missing value
            }
            string value = valueToken.ToObject<string>();

            if (!obj.TryGetValue("type", out var typeToken) || typeToken == null)
            {
                Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: missing type");
                return 0; // Invalid: missing type
            }
            string type = (string)typeToken;

            if (!obj.TryGetValue("typeId", out var typeIdToken) || !int.TryParse(typeIdToken?.ToString(), out var typeIdParsed))
            {
                Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: missing or invalid typeId");
                return 0; // Invalid: missing or invalid typeId
            }
            int typeId = typeIdParsed;

            if (!obj.TryGetValue("addr", out var addrToken) || !long.TryParse(addrToken?.ToString(), out var addrParsed))
            {
                Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: missing or invalid addr");
                return 0; // Invalid: missing or invalid addr
            }

            if (addrParsed == 0)
            {
                Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: addr is zero");
                return 0;
            }

            long addr = addrParsed;

            if (!obj.TryGetValue("size", out var sizeToken) || !int.TryParse(sizeToken?.ToString(), out var sizeParsed))
            {
                Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: missing or invalid size");
                return 0; // Invalid: missing or invalid size
            }
            int size = sizeParsed;

            // Optional paging fields validation and read
            int? start = null;
            if (obj.TryGetValue("start", out var startToken) && startToken != null)
            {
                if (!int.TryParse(startToken.ToString(), out var startVal))
                {
                    Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: invalid start");
                    return 0; // Invalid start
                }
                start = startVal;
            }
            int? count = null;
            if (obj.TryGetValue("count", out var countToken) && countToken != null)
            {
                if (!int.TryParse(countToken.ToString(), out var countVal))
                {
                    Debug.WriteLine($"{AD7Engine.ProtocolPrefix} Invalid {command} response: invalid count");
                    return 0; // Invalid count
                }
                count = countVal;
            }

            var pv = new ProtocolVariable
            {
                Name = name,
                Value = value,
                Type = type,
                Addr = addr,
                TypeId = typeId,
                Size = size,
                Elements = null
            };

            RemoteState.MemoryVariables[addr] = pv;

            // Parse elements recursively
            if (obj.TryGetValue("elements", out var elementsObj) && elementsObj is JArray jArray)
            {
                pv.Elements = new List<long>();
                foreach (var item in jArray)
                {
                    if (item is JObject childObj)
                    {
                        long childAddr = ParseAndStoreVariable(childObj, command);
                        if (childAddr != 0)
                        {
                            pv.Elements.Add(childAddr);
                        }
                    }
                }
            }

            return addr;
        }
    }
}
