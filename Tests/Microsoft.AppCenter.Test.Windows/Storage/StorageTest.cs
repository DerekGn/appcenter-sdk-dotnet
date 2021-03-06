// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AppCenter.Ingestion.Models;
using Microsoft.AppCenter.Ingestion.Models.Serialization;
using Microsoft.AppCenter.Storage;
using Microsoft.AppCenter.Test.Windows.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AppCenter.Test.Windows.Storage
{
    [TestClass]
    public class StorageTest
    {
        private IStorage _storage;
        private string _databasePath;

        private StorageTestUtils _storageUtils;

        // Const for storage data.
        private const string StorageTestChannelName = "storageTestChannelName";
        private const string TableName = "LogEntry";
        private const string ColumnChannelName = "Channel";
        private const string ColumnLogName = "Log";
        private const string ColumnIdName = "Id";


        [TestInitialize]
        public void TestInitialize()
        {
            _databasePath = $"{Guid.NewGuid()}.db";
            _storageUtils = new StorageTestUtils(_databasePath);
            Microsoft.AppCenter.Utils.Constants.AppCenterDatabasePath = _databasePath;
            Microsoft.AppCenter.Utils.Constants.AppCenterFilesDirectoryPath = Environment.CurrentDirectory;
            _storage = new Microsoft.AppCenter.Storage.Storage();
            _storage.DeleteLogs(StorageTestChannelName);
            _storage.WaitOperationsAsync(TimeSpan.FromSeconds(10)).Wait();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            Microsoft.AppCenter.Utils.Constants.AppCenterDatabasePath = "";
            Microsoft.AppCenter.Utils.Constants.AppCenterFilesDirectoryPath = "";
            try
            {
                _storage.DeleteLogs(StorageTestChannelName);
                _storage.WaitOperationsAsync(TimeSpan.FromSeconds(10)).Wait();
                _storage.Dispose();
                _storage = null;
            }
            catch
            {
                // ignored
            }
        }

        [TestMethod]
        public void TestDatabaseIsInitialized()
        {
            var mockStorageAdapter = Mock.Of<IStorageAdapter>();
            using (var storage = new Microsoft.AppCenter.Storage.Storage(mockStorageAdapter, _databasePath))
            {
                storage.WaitOperationsAsync(TimeSpan.FromSeconds(10)).Wait();

                // Verify database is initialized as a result of calling constructor.
                Mock.Get(mockStorageAdapter).Verify(adapter => adapter.CreateTable(TableName, It.IsAny<string[]>(), It.IsAny<string[]>()));
                Mock.Get(mockStorageAdapter).Verify(adapter => adapter.Initialize(It.IsAny<string>()));
            }
        }

        /// <summary>
        /// Verify that counting number of logs stored when there are no logs returns 0.
        /// </summary>
        [TestMethod]
        public void CountEmptyStorage()
        {
            var count = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(0, count);
        }

        /// <summary>
        /// Verify that after adding 'n' logs, counting logs returns 'n'.
        /// </summary>
        [TestMethod]
        public void CountNonemptyStorage()
        {
            var numLogsToAdd = 5;
            PutNLogs(numLogsToAdd);
            var count = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(numLogsToAdd, count);
        }

        /// <summary>
        /// Verify that storing a log and then retrieving it from storage does not alter the log.
        /// </summary>
        [TestMethod]
        public void PutOneLog()
        {
            var addedLog = TestLog.CreateTestLog();
            _storage.PutLog(StorageTestChannelName, addedLog).Wait();
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, 1, retrievedLogs).RunNotAsync();
            var retrievedLog = retrievedLogs[0];
            Assert.AreEqual(addedLog, retrievedLog);
        }

        /// <summary>
        /// Verify that any exception thrown by a task is converted to a storage exception.
        /// </summary>
        [TestMethod]
        public void UnknownExceptionIsConvertedToStorageException()
        {
            var mockStorageAdapter = Mock.Of<IStorageAdapter>();
            using (var storage = new Microsoft.AppCenter.Storage.Storage(mockStorageAdapter, _databasePath))
            {
                var exception = new Exception();
                Mock.Get(mockStorageAdapter).Setup(adapter => adapter.Count(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>())).Throws(exception);
                Mock.Get(mockStorageAdapter).Setup(adapter => adapter.Insert(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<IList<object[]>>())).Throws(exception);
                Assert.ThrowsException<StorageException>(() => storage.PutLog(StorageTestChannelName, TestLog.CreateTestLog()).RunNotAsync());
                Assert.ThrowsException<StorageException>(() => storage.CountLogsAsync(StorageTestChannelName).RunNotAsync());
            }
        }

        /// <summary>
        /// Verify that any exception thrown by a task is returned as is if already storage exception.
        /// </summary>
        [TestMethod]
        public void KnownExceptionIsThrownAsIs()
        {
            var mockStorageAdapter = Mock.Of<IStorageAdapter>();
            var exception = new StorageException();
            using (var storage = new Microsoft.AppCenter.Storage.Storage(mockStorageAdapter, _databasePath))
            {
                Mock.Get(mockStorageAdapter).Setup(adapter => adapter.Count(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>())).Throws(exception);
                Mock.Get(mockStorageAdapter).Setup(adapter => adapter.Insert(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<IList<object[]>>())).Throws(exception);
                Assert.ThrowsException<StorageException>(() =>
                    storage.PutLog(StorageTestChannelName, TestLog.CreateTestLog()).RunNotAsync());
                Assert.ThrowsException<StorageException>(() =>
                   storage.CountLogsAsync(StorageTestChannelName).RunNotAsync());
            }
        }

        /// <summary>
        /// Verify that deleting all logs for a given channel does so.
        /// </summary>
        [TestMethod]
        public void DeleteLogsNoBatchId()
        {
            PutNLogs(5);
            _storage.DeleteLogs(StorageTestChannelName);
            var count = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(0, count);
        }

        /// <summary>
        /// Verify that deleting a particular batch deletes exactly the number of logs for that batch.
        /// </summary>
        [TestMethod]
        public void DeleteLogsWithBatchId()
        {
            var numLogsToAdd = 5;
            var limit = 3;
            _ = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            _storage.DeleteLogs(StorageTestChannelName, batchId);
            var numLogsRemaining = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(numLogsToAdd - retrievedLogs.Count, numLogsRemaining);
        }

        /// <summary>
        /// Verify that when the limit equals the number of logs for the given channel, all logs are returned.
        /// </summary>
        [TestMethod]
        public void GetLogsExactLimit()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogs);
        }

        /// <summary>
        /// Verify that when the limit is lower than the number of logs for the given channel, all logs are returned.
        /// </summary>
        [TestMethod]
        public void GetLogsLowLimit()
        {
            var numLogsToAdd = 5;
            var limit = 3;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            Assert.AreEqual(limit, retrievedLogs.Count);
            CollectionAssert.IsSubsetOf(retrievedLogs, addedLogs);
        }

        /// <summary>
        /// Verify that when the limit exceeds the number of logs for the given channel, 'limit' logs are correctly returned.
        /// </summary>
        [TestMethod]
        public void GetLogsHighLimit()
        {
            var numLogsToAdd = 5;
            var limit = 7;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            CollectionAssert.AreEquivalent(retrievedLogs, addedLogs);
        }

        /// <summary>
        /// Verify that pending logs are not returned but other logs are.
        /// </summary>
        [TestMethod]
        public void GetLogsExcludesPendingLogsWithoutAffectingLimit()
        {
            var numLogsToAdd = 5;
            var limit = 5;

            // Add some logs and then retrieve them so they are marked as pending.
            PutNLogs(numLogsToAdd);
            _storage.GetLogsAsync(StorageTestChannelName, limit, new List<Log>()).RunNotAsync();

            // Add some new logs.
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();

            //  Retrieve logs and make sure all of the new ones are returned, but not the pending logs.
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogs);
        }

        /// <summary>
        /// Verify that when logs are retrieved, the batchId is not null.
        /// </summary>
        [TestMethod]
        public void GetLogsHasBatchId()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            _ = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            Assert.IsNotNull(batchId);
        }

        /// <summary>
        /// Verify that when no logs are retrieved, the batchId is null.
        /// </summary>
        [TestMethod]
        public void GetNoLogsHasNoBatchId()
        {
            var numLogsToAdd = 0;
            var limit = numLogsToAdd;
            _ = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            Assert.IsNull(batchId);
        }

        /// <summary>
        /// Verify that storage does not return same log more than once.
        /// </summary>
        [TestMethod]
        public void GetDuplicateLogs()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogsFirstTry = new List<Log>();
            var retrievedLogsSecondTry = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsFirstTry).RunNotAsync();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsSecondTry).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogsFirstTry);
            Assert.AreEqual(0, retrievedLogsSecondTry.Count);
        }

        /// <summary>
        /// Verify that a channel that starts with the name of another channel does not cause problems.
        /// </summary>
        [TestMethod]
        public void GetLogsFromChannelWithSimilarNames()
        {
            var fakeChannelName = StorageTestChannelName.Substring(0, StorageTestChannelName.Length - 1);
            _storage.PutLog(StorageTestChannelName, TestLog.CreateTestLog()).Wait();
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(fakeChannelName, 1, retrievedLogs).RunNotAsync();
            Assert.IsNull(batchId);
        }

        /// <summary>
        /// Verify that storage returns log more than once if pending state is cleared.
        /// </summary>
        [TestMethod]
        public void ClearPendingState()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogsFirstTry = new List<Log>();
            var retrievedLogsSecondTry = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsFirstTry).RunNotAsync();
            _storage.ClearPendingLogState(StorageTestChannelName);
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsSecondTry).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogsFirstTry);
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogsSecondTry);
        }

        /// <summary>
        /// Verify that an invalid log in the database, when retrieved, is deleted and no logs are returned.
        /// </summary>
        [TestMethod]
        public void FailToGetALog()
        {
            // Prepare data.
            StorageAdapter adapter = new StorageAdapter();
            adapter.Initialize(_databasePath);
            var tables = new[] { ColumnIdName, ColumnChannelName, ColumnLogName };
            var types = new[] { "INTEGER PRIMARY KEY AUTOINCREMENT", "TEXT NOT NULL", "TEXT NOT NULL" };
            adapter.CreateTable(TableName, tables, types);
            adapter.Insert(TableName, tables, new List<object[]> { new object[] { 100, StorageTestChannelName, "good luck deserializing me!" } });
            var storage = new Microsoft.AppCenter.Storage.Storage(adapter, _databasePath);
            var logs = new List<Log>();
            var batchId = storage.GetLogsAsync(StorageTestChannelName, 4, logs).RunNotAsync();
            var count = storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.IsNull(batchId);
            Assert.AreEqual(0, logs.Count);
            Assert.AreEqual(0, count);
        }

        /// <summary>
        /// Verify that we recreated corrupted database.
        /// </summary>
        [TestMethod]
        public async Task RecreateCorruptedDatabaseOnInnerCorruptException()
        {
            var mockStorageAdapter = Mock.Of<IStorageAdapter>();
            using (var storage = new Microsoft.AppCenter.Storage.Storage(mockStorageAdapter, _databasePath))
            {
                var exception = new StorageCorruptedException("Mock exception");
                var testLog = TestLog.CreateTestLog();
                var testLogString = LogSerializer.Serialize(testLog);
                var testLogSize = Encoding.UTF8.GetBytes(testLogString).Length;
                Mock.Get(mockStorageAdapter).Setup(adapter => adapter.GetMaxStorageSize()).Returns(testLogSize + 1);
                Mock.Get(mockStorageAdapter).Setup(adapter => adapter.Insert(TableName, It.IsAny<string[]>(), It.IsAny<List<object[]>>())).Throws(exception);
                await Assert.ThrowsExceptionAsync<StorageCorruptedException>(() => storage.PutLog(StorageTestChannelName, testLog));
                Mock.Get(mockStorageAdapter).Verify(adapter => adapter.Dispose());
                Mock.Get(mockStorageAdapter).Verify(adapter => adapter.Initialize(It.IsAny<string>()), Times.Exactly(2));
            }
        }

        /// <summary>
        /// Verify that we don't delete database if the error is not related to corruption.
        /// </summary>
        [TestMethod]
        public async Task DoNotRecreateCorruptedDatabaseOnNotCorruptException()
        {
            var mockStorageAdapter = Mock.Of<IStorageAdapter>();
            using (var storage = new Microsoft.AppCenter.Storage.Storage(mockStorageAdapter, _databasePath))
            {
                var exception = new Exception("Something else");
                Mock.Get(mockStorageAdapter).Setup(adapter => adapter.Insert(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<List<object[]>>())).Throws(exception);
                await Assert.ThrowsExceptionAsync<StorageException>(() => storage.PutLog(StorageTestChannelName, TestLog.CreateTestLog()));
                Mock.Get(mockStorageAdapter).Verify(adapter => adapter.Dispose(), Times.Never());
                Mock.Get(mockStorageAdapter).Verify(adapter => adapter.Initialize(It.IsAny<string>()), Times.Once());
            }
        }

        /// <summary>
        /// Verify that Storage forwards SetMaxStorageSize to its storageAdapter.
        /// </summary>
        [TestMethod]
        public async Task SetMaxStorageSize()
        {
            var mockStorageAdapter = Mock.Of<IStorageAdapter>();
            using (var storage = new Microsoft.AppCenter.Storage.Storage(mockStorageAdapter, _databasePath))
            {
                var dbSize = 2 * 1024 * 1024;
                _ = await storage.SetMaxStorageSizeAsync(dbSize);
                Mock.Get(mockStorageAdapter).Verify(adapter => adapter.SetMaxStorageSize(dbSize), Times.Once());
            }
        }

        /// <summary>
        /// Verify that new Logs are stored if storage is not full.
        /// </summary>
        [TestMethod]
        public async Task AddLogsWhenBelowStorageCapacity()
        {
            // Set storage max size.
            var capacity = 24 * 1024;
            var logsDataSize = 12 * 1024;
            var setMaxStorageSizeResult = await _storage.SetMaxStorageSizeAsync(logsDataSize);
            Assert.IsTrue(setMaxStorageSizeResult);

            // Fill databse with logs.
            await FillDatabaseWithLogs(logsDataSize);

            // Change storage max size.
            setMaxStorageSizeResult = await _storage.SetMaxStorageSizeAsync(capacity);
            Assert.IsTrue(setMaxStorageSizeResult);

            // Create a new logs and verify that they could be received.
            var numLogsToAdd = 10;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            await _storage.GetLogsAsync(StorageTestChannelName, int.MaxValue, retrievedLogs);
            CollectionAssert.IsSubsetOf(addedLogs, retrievedLogs);
            var dataSizeInBytes = _storageUtils.GetDataLengthInBytes();
            Assert.IsTrue(dataSizeInBytes > logsDataSize);
        }

        /// <summary>
        /// Verify that saving new logs does not exceed DB capacity.
        /// </summary>
        [TestMethod]
        public async Task AddLogsDoesNotExceedCapacity()
        {
            // Set storage max size.
            var capacity = 12 * 1024;
            var setMaxStorageSizeResult = await _storage.SetMaxStorageSizeAsync(capacity);
            Assert.IsTrue(setMaxStorageSizeResult);

            // Fill databse with logs.
            await FillDatabaseWithLogs(capacity);

            // Put new logs and verify that database size can't be more then max size.
            var numLogsToAdd = 10;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var dataSizeInBytes = _storageUtils.GetDataLengthInBytes();
            Assert.IsTrue(dataSizeInBytes <= capacity);
            var retrievedLogs = new List<Log>();

            // Verify that added logs could be received.
            await _storage.GetLogsAsync(StorageTestChannelName, int.MaxValue, retrievedLogs);
            CollectionAssert.IsSubsetOf(addedLogs, retrievedLogs);
        }

        /// <summary>
        /// Verify that new logs purge old ones.
        /// </summary>
        [TestMethod]
        public async Task SaveLogPurgesOldestLogsWhenStorageIsFull()
        {
            // Set storage max size.
            var capacity = 12 * 1024;
            var setMaxStorageSizeResult = await _storage.SetMaxStorageSizeAsync(capacity);
            Assert.IsTrue(setMaxStorageSizeResult);

            // Fill databse with logs.
            var initialLogs = await FillDatabaseWithLogs(capacity);
            var firstLog = initialLogs[0];

            // Create a new log. Vverify that new log was added and old was deleted.
            var newLog = TestLog.CreateTestLog();
            await _storage.PutLog(StorageTestChannelName, newLog);
            var retrievedLogs = new List<Log>();
            await _storage.GetLogsAsync(StorageTestChannelName, int.MaxValue, retrievedLogs);
            CollectionAssert.Contains(retrievedLogs, newLog);
            CollectionAssert.DoesNotContain(retrievedLogs, firstLog);
        }

        /// <summary>
        /// Verify that new large log does not purge old ones.
        /// </summary>
        [TestMethod]
        public async Task SaveLargeLogDoesNotPurgeOtherLogsWhenStorageIsFull()
        {
            // Set storage max size.
            var capacity = 12 * 1024;
            var setMaxStorageSizeResult = await _storage.SetMaxStorageSizeAsync(capacity);
            Assert.IsTrue(setMaxStorageSizeResult);

            // Put 2 logs into databse.
            var logs = PutNLogs(2);

            // Try to put large log into databse and verify that StorageException exception was trown.
            var largeLogSize = capacity + 1;
            var largeLog = CreateLogWithSize(largeLogSize);
            try 
            {
               await _storage.PutLog(StorageTestChannelName, largeLog);
            }
            catch (StorageException e)
            {
                var logJsonString = LogSerializer.Serialize(largeLog);
                var logSize = Encoding.UTF8.GetBytes(logJsonString).Length;
                Assert.AreEqual(e.Message, $"Log is too large ({logSize} bytes) to store in database. Current maximum database size is {capacity} bytes.");
            }

            // Verify that logs in databse were not deleted.
            var retrievedLogs = new List<Log>();
            await _storage.GetLogsAsync(StorageTestChannelName, int.MaxValue, retrievedLogs);
            CollectionAssert.Contains(retrievedLogs, logs[0]);
            CollectionAssert.Contains(retrievedLogs, logs[1]);
            CollectionAssert.DoesNotContain(retrievedLogs, largeLog);
        }

        #region Helper methods

        private List<TestLog> PutNLogs(int n)
        {
            var putLogTasks = new Task[n];
            var addedLogs = new List<TestLog>();
            for (var i = 0; i < n; ++i)
            {
                var testLog = TestLog.CreateTestLog();
                addedLogs.Add(testLog);
                putLogTasks[i] = _storage.PutLog(StorageTestChannelName, testLog);
            }
            return addedLogs;
        }

        private TestLog CreateLogWithSize(int size)
        {
            var testLog = TestLog.CreateTestLog();

            // There is a limitation for string size, so we tear into pieces.
            var propsNum = size / 1000 + 1; 
            for (var i = 0; i < propsNum; i++)
            {
                testLog.Properties.Add($"largeProp{i}", new string('.', size));
            }
            return testLog;
        }

        private async Task<List<TestLog>> FillDatabaseWithLogs(int storageCapacity)
        {
            var log = TestLog.CreateTestLog();
            var logJsonString = LogSerializer.Serialize(log);
            var logSize = Encoding.UTF8.GetBytes(logJsonString).Length;
            var logsCount = storageCapacity / logSize;
            var logs = PutNLogs(logsCount - 1);
            logs.Add(log);
            await _storage.PutLog(StorageTestChannelName, log);
            return logs;
        }

        #endregion
    }
}
