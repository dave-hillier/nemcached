﻿using System.Reactive.Concurrency;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nemcache.Service;

namespace Nemcache.Tests.RequestHandlerIntegrationTests
{
    [TestClass]
    public class VersionRequestTests
    {
        private RequestHandler _requestHandler;

        [TestInitialize]
        public void Setup()
        {
            _requestHandler = new RequestHandler(Scheduler.Default, new MemCache(10));
        }

        private byte[] Dispatch(byte[] p)
        {
            return _requestHandler.Dispatch("", p, null);
        }


        [TestMethod]
        public void Version()
        {
            var flushRequest = Encoding.ASCII.GetBytes("version\r\n");
            var response = Dispatch(flushRequest);
            Assert.AreEqual("Nemcache 1.0.0.0\r\n", response.ToAsciiString());
        }
    }
}