// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient.ManualTesting.Tests;
using Microsoft.Data.SqlClient.Tests.Common.Fixtures.DatabaseObjects;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTests.BulkCopy
{
    // Cancels a bulk copy in the MIDDLE of the operation (not with a pre-cancelled
    // token). The cancellation is requested from the SqlRowsCopied notification
    // after the first batch while all writes are forced to pend, so the cancel is
    // observed by the CopyRowsAsync/CheckForCancellation continuation path. Verifies
    // the returned Task ends cancelled and the connection remains usable afterwards.
    [Trait("Set", "2")]
    public class ForcePendMidCopyCancelAsync
    {
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup), nameof(DataTestUtility.IsNotAzureServer))]
        public void Test()
        {
#if DEBUG
            string dstConstr = DataTestUtility.TCPConnectionString;
            Task t = TestAsync(dstConstr);
            // Cancellation surfaces as a cancelled task.
            DataTestUtility.AssertThrowsInner<AggregateException, TaskCanceledException>(() => t.Wait());
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
                "BulkCopy_ForcePendMidCopyCancelAsync",
                AsyncContinuationHelpers.ThreeColumnTableDefinition);

            using CancellationTokenSource cts = new CancellationTokenSource();

            try
            {
                using (SqlBulkCopy bulkcopy = new SqlBulkCopy(dstConn))
                {
                    bulkcopy.DestinationTableName = dstTable.Name;
                    bulkcopy.BatchSize = 25;
                    bulkcopy.NotifyAfter = 25;
                    bulkcopy.ColumnMappings.Add(0, "col1");
                    bulkcopy.ColumnMappings.Add(1, "col2");
                    bulkcopy.ColumnMappings.Add(2, "col3");

                    // Cancel mid-stream, once the first notification fires.
                    bulkcopy.SqlRowsCopied += (sender, e) => cts.Cancel();

                    using AsyncDebugScope debugScope = new AsyncDebugScope { ForceAllPends = true };

                    await bulkcopy.WriteToServerAsync(source, cts.Token);
                }
            }
            finally
            {
                // The connection must still be usable after a cancelled bulk copy.
                using SqlCommand ping = new SqlCommand("SELECT 1", dstConn);
                object result = ping.ExecuteScalar();
                DataTestUtility.AssertEqualsWithDescription(1, result, "Connection unusable after cancellation.");
            }
        }
#endif
    }
}
