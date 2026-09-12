// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data;
using Microsoft.Data.SqlClient.ManualTesting.Tests;
using Microsoft.Data.SqlClient.Tests.Common.Fixtures.DatabaseObjects;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTests.BulkCopy
{
    // Aborts a bulk copy by setting SqlRowsCopiedEventArgs.Abort = true from the
    // notification handler, while all writes are forced to pend. This drives the
    // abort branch of CheckAndRaiseNotification through the async continuation
    // path (distinct from token cancellation and from connection close). The
    // operation must surface OperationAbortedException and leave the connection
    // usable.
    [Trait("Set", "2")]
    public class ForcePendAbortFromEventAsync
    {
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup), nameof(DataTestUtility.IsNotAzureServer))]
        public void Test()
        {
#if DEBUG
            string dstConstr = DataTestUtility.TCPConnectionString;
            Task t = TestAsync(dstConstr);
            DataTestUtility.AssertThrowsInner<AggregateException, OperationAbortedException>(() => t.Wait());
            Assert.True(t.IsCompleted, "Task did not complete! Status: " + t.Status);
#endif
        }

#if DEBUG
        private static async Task TestAsync(string dstConstr)
        {
            const int rowCount = 200;

            DataTable source = AsyncContinuationHelpers.BuildThreeColumnSource(rowCount);

            using SqlConnection dstConn = new SqlConnection(dstConstr);
            dstConn.Open();

            using Table dstTable = new Table(
                dstConn,
                "BulkCopy_ForcePendAbortFromEventAsync",
                AsyncContinuationHelpers.ThreeColumnTableDefinition);

            try
            {
                using SqlBulkCopy bulkcopy = new SqlBulkCopy(dstConn);
                bulkcopy.DestinationTableName = dstTable.Name;
                bulkcopy.BatchSize = 25;
                bulkcopy.NotifyAfter = 25;
                bulkcopy.ColumnMappings.Add(0, "col1");
                bulkcopy.ColumnMappings.Add(1, "col2");
                bulkcopy.ColumnMappings.Add(2, "col3");

                bulkcopy.SqlRowsCopied += (sender, e) => e.Abort = true;

                using AsyncDebugScope debugScope = new AsyncDebugScope { ForceAllPends = true };

                await bulkcopy.WriteToServerAsync(source);
            }
            finally
            {
                using SqlCommand ping = new SqlCommand("SELECT 1", dstConn);
                object result = ping.ExecuteScalar();
                DataTestUtility.AssertEqualsWithDescription(1, result, "Connection unusable after abort.");
            }
        }
#endif
    }
}
