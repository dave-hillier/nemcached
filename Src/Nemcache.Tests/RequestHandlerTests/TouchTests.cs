﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Concurrency;
using Microsoft.Reactive.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nemcache.Client.Builders;
using Nemcache.Service;
using Nemcache.Service.RequestHandlers;

namespace Nemcache.Tests.RequestHandlerIntegrationTests
{
    [TestClass]
    public class TouchTests
    {
        private RequestDispatcher _requestDispatcher;
        private TestScheduler _testScheduler;

        private byte[] Dispatch(byte[] p)
        {
            var memoryStream = new MemoryStream(1024);
            _requestDispatcher.Dispatch(new MemoryStream(p), memoryStream, "", null).Wait();
            return memoryStream.ToArray();
        }

        [TestInitialize]
        public void Setup()
        {
            _testScheduler = new TestScheduler();
            IMemCache cache = new MemCache(capacity: 100);
            
            _requestDispatcher = new RequestDispatcher(_testScheduler, cache, Service.Service.GetRequestHandlers(_testScheduler, cache));
        }

        #region touch

        [TestMethod]
        public void TouchOk()
        {
            var storageBuilder = new StoreRequestBuilder("set", "key", "value");
            storageBuilder.WithExpiry(100);
            Dispatch(storageBuilder.ToAsciiRequest());

            var touchBuilder = new TouchRequestBuilder("key");
            touchBuilder.WithExpiry(1);
            var response = Dispatch(touchBuilder.ToAsciiRequest());

            Assert.AreEqual("OK\r\n", response.ToAsciiString());
        }

        [TestMethod]
        public void TouchNotFound()
        {
            var touchBuilder = new TouchRequestBuilder("key");
            touchBuilder.WithExpiry(1);
            var response = Dispatch(touchBuilder.ToAsciiRequest());

            Assert.AreEqual("NOT_FOUND\r\n", response.ToAsciiString());
        }

        [TestMethod]
        public void SetTouchExpiryThenGetGone()
        {
            var storageBuilder = new StoreRequestBuilder("set", "key", "value");
            storageBuilder.WithExpiry(100);
            Dispatch(storageBuilder.ToAsciiRequest());

            var touchBuilder = new TouchRequestBuilder("key");
            touchBuilder.WithExpiry(1);
            Dispatch(touchBuilder.ToAsciiRequest());
            _testScheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);

            var getBuilder = new GetRequestBuilder("get", "key");
            var response = Dispatch(getBuilder.ToAsciiRequest());
            Assert.AreEqual("END\r\n", response.ToAsciiString());
        }

        #endregion
    }
}