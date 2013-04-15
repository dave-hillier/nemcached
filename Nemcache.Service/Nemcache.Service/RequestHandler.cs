﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nemcache.Service
{
    internal class RequestHandler
    {
        public RequestHandler()
        {
            Capacity = 1024 * 1024 * 100;
        }

        private readonly byte[] EndOfLine = new byte[] { 13, 10 }; // Ascii for "\r\n"
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

        private struct CacheEntry
        {
            public ulong Flags { get; set; }
            public DateTime Expiry { get; set; }
            public ulong CasUnique { get; set; }
            public byte[] Data { get; set; }

            public bool IsExpired { get { return Expiry < Scheduler.Current.Now; } }
        }

        public IEnumerable<byte> TakeFirstLine(byte[] request)
        {
            int endOfLineIndex = -1;
            for (int i = 0; i < request.Length; ++i)
            {
                if (request[i + 0] == EndOfLine[0] &&
                    request[i + 1] == EndOfLine[1])
                {
                    endOfLineIndex = i;
                    break;
                }
            }
            if (endOfLineIndex != -1)
                return request.Take(endOfLineIndex);
            else
                throw new Exception("New line not found"); // TODO: better exception type.
        }

        static DateTime UnixTimeEpoc = new DateTime(1970, 1, 1, 0, 0, 0, 0);

        public DateTime ToExpiry(string expiry)
        {
            var expirySeconds = uint.Parse(expiry);
            // up to 60*60*24*30 seconds or unix time
            if (expirySeconds == 0)
                return DateTime.MaxValue;
            var start = expirySeconds < 60 * 60 * 24 * 30 ? Scheduler.Current.Now : UnixTimeEpoc;
            return start + TimeSpan.FromSeconds(expirySeconds);
        }

        public string ToKey(string key)
        {
            if (key.Length > 250)
                throw new InvalidOperationException("Key too long");
            // TODO: no control chars
            return key;
        }

        public ulong ToFlags(string flags)
        {
            return ulong.Parse(flags);
        }

        public byte[] Dispatch(string remoteEndpoint, byte[] request)
        {
            try
            {
                var input = TakeFirstLine(request).ToArray();
                var requestFirstLine = Encoding.ASCII.GetString(input);
                var requestTokens = requestFirstLine.Split(' ');
                var commandName = requestTokens.First();
                var commandParams = requestTokens.Skip(1).ToArray();
                bool noreply = commandParams.LastOrDefault() == "noreply";
                var result = HandleCommand(request, input, commandName, commandParams);
                return noreply ? new byte[] { } : result;
            }
            catch (Exception ex)
            {
                return Encoding.ASCII.GetBytes(string.Format("SERVER_ERROR {0}\r\n", ex.Message));
            }
        }

        private byte[] HandleCommand(byte[] request, byte[] input, string commandName, string[] commandParams)
        {
            switch (commandName)
            {
                case "get":
                case "gets": // <key>*
                    return HandleGet(commandParams);
                case "add":
                case "replace":
                case "append":
                case "prepend":
                case "set": // <command name> <key> <flags> <exptime> <bytes> [noreply]
                    return HandleStore(request, input, commandName, commandParams);
                case "cas"://cas <key> <flags> <exptime> <bytes> <cas unique> [noreply]\r\n
                    return HandleCas(request, input, commandParams);
                case "delete": // delete <key> [noreply]\r\n
                    return HandleDelete(commandParams);
                case "incr"://incr <key> <value> [noreply]\r\n
                case "decr":
                    return HandleIncr(commandName, commandParams);
                case "touch"://touch <key> <exptime> [noreply]\r\n
                    return HandleTouch(commandParams);
                case "stats":// stats <args>\r\n or stats\r\n
                    return HandleStats(commandName, commandParams);
                case "flush_all": // [numeric] [noreply]
                    return HandleFlushAll(commandParams);
                case "quit": //     quit\r\n
                    return new byte[] { };
                default:
                    return Encoding.ASCII.GetBytes("ERROR\r\n");
            }
        }

        private byte[] HandleStats(string commandName, string[] commandParams)
        {
            return new byte[] { };
        }

        private byte[] HandleFlushAll(string[] commandParams)
        {
            // TODO: delay by param then reset. 

            _cache.Clear();
            return Encoding.ASCII.GetBytes("OK\r\n");
        }

        private byte[] HandleTouch(string[] commandParams)
        {
            var key = ToKey(commandParams[0]);
            var exptime = ToExpiry(commandParams[1]);
            CacheEntry entry;
            if (_cache.TryGetValue(key, out entry))
            {
                entry.Expiry = exptime;
                _cache[key] = entry;
                byte[] result = Encoding.ASCII.GetBytes("OK");
                return result.Concat(EndOfLine).ToArray();
            }
            return Encoding.ASCII.GetBytes("NOT_FOUND\r\n");
        }

        private byte[] HandleIncr(string commandName, string[] commandParams)
        {
            var key = ToKey(commandParams[0]);
            var incr = ulong.Parse(commandParams[1]);
            CacheEntry entry;
            if (_cache.TryGetValue(key, out entry))
            {
                var value = ulong.Parse(Encoding.ASCII.GetString(entry.Data));
                if (commandName == "incr")
                    value += incr;
                else
                    value -= incr;
                var result = Encoding.ASCII.GetBytes(value.ToString());
                entry.Data = result;
                _cache[key] = entry;
                return result.Concat(EndOfLine).ToArray();
            }
            return Encoding.ASCII.GetBytes("NOT_FOUND\r\n");
        }

        private byte[] HandleDelete(string[] commandParams)
        {
            var key = ToKey(commandParams[0]);
            CacheEntry entry;
            if (_cache.TryGetValue(key, out entry))
            {
                _cache.Remove(key);
                return Encoding.ASCII.GetBytes("DELETED\r\n");
            }
            return Encoding.ASCII.GetBytes("NOT_FOUND\r\n");
        }

        private byte[] HandleCas(byte[] request, byte[] input, string[] commandParams)
        {
            var key = ToKey(commandParams[0]);
            var flags = ToFlags(commandParams[1]);
            var exptime = ToExpiry(commandParams[2]);
            var bytes = int.Parse(commandParams[3]);
            var casUnique = ulong.Parse(commandParams[4]);
            byte[] data = request.Skip(input.Length + 2).Take(bytes).ToArray();
            return new byte[] { };
        }

        // TODO: consider wrapping all these parameters in a request type
        private byte[] HandleStore(byte[] request, byte[] input, string commandName, string[] commandParams)
        {
            var key = ToKey(commandParams[0]);
            var flags = ToFlags(commandParams[1]);
            var exptime = ToExpiry(commandParams[2]);
            var bytes = int.Parse(commandParams[3]);
            byte[] data = request.Skip(input.Length + 2).Take(bytes).ToArray();

            CacheEntry entry;
            switch (commandName)
            {
                case "set":
                    return Store(key, flags, exptime, data);
                case "replace":
                    if (_cache.TryGetValue(key, out entry))
                    {
                        return Store(key, flags, exptime, data);
                    }
                    return Encoding.ASCII.GetBytes("NOT_STORED\r\n");
                case "add":
                    if (!_cache.TryGetValue(key, out entry))
                    {
                        return Store(key, flags, exptime, data);
                    }
                    return Encoding.ASCII.GetBytes("NOT_STORED\r\n");

                case "append":
                case "prepend":
                    if (_cache.TryGetValue(key, out entry))
                    {
                        var newData = commandName == "append" ?
                            entry.Data.Concat(data) :
                            data.Concat(entry.Data);
                        return Store(key, entry.Flags, entry.Expiry, newData.ToArray());
                    }
                    else
                    {
                        return Store(key, flags, exptime, data);
                    }
            }

            return Encoding.ASCII.GetBytes("ERROR\r\n");
        }

        private byte[] Store(string key, ulong flags, DateTime exptime, byte[] data)
        {
            if (data.Length > Capacity)
            {
                return Encoding.ASCII.GetBytes("ERROR Over capacity\r\n");
            }

            while (Used + data.Length > Capacity)
            {
                var keyToEvict = _cache.Keys.OrderBy(k => Guid.NewGuid()).First();
                _cache.Remove(keyToEvict);
            }

            var entry = new CacheEntry { Data = data, Expiry = exptime, Flags = flags };
            _cache[key] = entry;
            return Encoding.ASCII.GetBytes("STORED\r\n");
        }

        private byte[] HandleGet(string[] commandParams)
        {
            var keys = commandParams.Select(ToKey);

            var entries = from key in keys
                          where _cache.ContainsKey(key)
                          select new { Key = key, CacheEntry = _cache[key] };

            var response = from entry in entries
                           where !entry.CacheEntry.IsExpired
                           let valueText = string.Format("VALUE {0} {1} {2}{3}\r\n",
                               entry.Key,
                               entry.CacheEntry.Flags,
                               entry.CacheEntry.Data.Length,
                               entry.CacheEntry.CasUnique != 0 ? " " + entry.CacheEntry.CasUnique : "")
                           let asAscii = Encoding.ASCII.GetBytes(valueText)
                           select asAscii.Concat(entry.CacheEntry.Data).Concat(EndOfLine);

            var endOfMessage = Encoding.ASCII.GetBytes("END");
            return response.SelectMany(a => a).Concat(endOfMessage).Concat(EndOfLine).ToArray();
        }

        public int Capacity { get; set; }

        public int Used
        {
            get
            {
                return _cache.Values.Select(e => e.Data.Length).Sum();
            }
        }
    }

}