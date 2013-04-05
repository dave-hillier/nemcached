﻿using System;
using System.Linq;
using System.Text;

namespace Nemcache.Service
{
    class AsciiRequest : IRequest
    {
        public AsciiRequest(byte[] input)
        {
            int firstLineLength = 0;
            for (int i = 0; i < input.Length-1; ++i)
            {
                if (input[i] == '\r' && input[i + 1] == '\n')
                {
                    firstLineLength = i;
                    break;
                }
            }

            var le = input.Take(firstLineLength);
            var line = Encoding.ASCII.GetString(le.ToArray());
            var tokens = line.Split();
            Command = tokens[0];
            Key = tokens[1];
            int length = input.Length - firstLineLength - 4;
            Data = new byte[length];
            Array.Copy(input, firstLineLength + 2, Data, 0, length);
        }

        public string Command { get; private set; }

        public string Key { get; private set; }

        public byte[] Data { get; private set; }
    }
}