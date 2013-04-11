﻿namespace Nemcache.Service.Commands
{
    internal class IncrCommand : ICommand
    {
        private readonly IArrayCache _arrayCache;

        public IncrCommand(IArrayCache arrayCache)
        {
            _arrayCache = arrayCache;
        }

        public string Name { get { return "incr"; } }
        public byte[] Execute(IRequest request)
        {
            throw new System.NotImplementedException();
        }
    }
}