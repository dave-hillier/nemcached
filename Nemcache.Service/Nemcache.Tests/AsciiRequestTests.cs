﻿using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nemcache.Service;

namespace Nemcache.Tests
{
    [TestClass]
    public class AsciiRequestTests
    {
        [TestMethod]
        public void CreateSetRequest()
        {
            var input = new MemcacheCommandBuilder("set", "some_key", new byte[] { 1, 2, 3, 4 }).ToRequest();

            var request = new AsciiRequest(input);

            Assert.AreEqual("set", request.CommandName);
            Assert.AreEqual("some_key", request.Key);
            Assert.IsTrue(new byte[] { 1, 2, 3, 4 }.SequenceEqual(request.Data));
        }

        [TestMethod]
        public void CreateSetRequestWithBigBuffer()
        {
            var inputSmall = new MemcacheCommandBuilder("set", "some_key", new byte[] { 1, 2, 3, 4 }).ToRequest();
            var input = inputSmall.Concat(new byte[10]).ToArray();
            var request = new AsciiRequest(input);

            Assert.AreEqual("set", request.CommandName);
            Assert.AreEqual("some_key", request.Key);
            Assert.IsTrue(new byte[] { 1, 2, 3, 4 }.SequenceEqual(request.Data));
        }

        [TestMethod]
        public void CreateGetRequest()
        {
            var input = new MemcacheGetCommandBuilder("get", "some_key").ToRequest();
            var request = new AsciiRequest(input);

            Assert.AreEqual("get", request.CommandName);
            Assert.AreEqual("some_key", request.Key);
        }

        // TODO: various invalid request
        // TODO: various flags
        // TODO: seconds expiry
        // TODO: unix time expiry

        // TODO: get requests
    }
}