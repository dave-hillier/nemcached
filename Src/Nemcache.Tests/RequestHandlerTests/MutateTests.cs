﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nemcache.Client.Builders;

namespace Nemcache.Tests.RequestHandlerIntegrationTests
{
    [TestClass]
    public class MutateTests
    {
        private IClient _client;

        public IClient Client { get; set; }

        private byte[] Dispatch(byte[] p)
        {
            return _client.Send(p);
        }

        [TestInitialize]
        public void Setup()
        {
            _client = Client ?? new LocalRequestHandlerWithTestScheduler();
        }

        [TestMethod]
        public void IncrNotFound()
        {
            var builder = new MutateRequestBuilder("incr", "key", 1);
            var response = Dispatch(builder.ToAsciiRequest());

            Assert.AreEqual("NOT_FOUND\r\n", response.ToAsciiString());
        }

        [TestMethod]
        public void DecrNotFound()
        {
            var builder = new MutateRequestBuilder("decr", "key", 1);
            var response = Dispatch(builder.ToAsciiRequest());

            Assert.AreEqual("NOT_FOUND\r\n", response.ToAsciiString());
        }

        [TestMethod]
        public void Incr()
        {
            var storageBuilder = new StoreRequestBuilder("set", "key", "123");
            Dispatch(storageBuilder.ToAsciiRequest());
            var builder = new MutateRequestBuilder("incr", "key", 1);
            var response = Dispatch(builder.ToAsciiRequest());
            Assert.AreEqual("124\r\n", response.ToAsciiString());
        }

        [TestMethod]
        public void Decr()
        {
            var storageBuilder = new StoreRequestBuilder("set", "key", "123");
            Dispatch(storageBuilder.ToAsciiRequest());
            var builder = new MutateRequestBuilder("decr", "key", 1);
            var response = Dispatch(builder.ToAsciiRequest());
            Assert.AreEqual("122\r\n", response.ToAsciiString());
        }

        // TODO: incr/decr max and overflow, non-int start value
    }
}