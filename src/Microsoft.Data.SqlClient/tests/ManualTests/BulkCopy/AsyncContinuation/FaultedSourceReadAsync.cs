// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient.ManualTesting.Tests;
using Microsoft.Data.SqlClient.Tests.Common.Fixtures.DatabaseObjects;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTests.BulkCopy
{
    // A source DbDataReader that throws from ReadAsync after a set number of rows.
    // Used to verify that a failure while reading the next row surfaces as a
    // faulted WriteToServerAsync task rather than hanging or being swallowed.
    internal sealed class ThrowingDbDataReader : DbDataReader
    {
        private readonly DataTable _table;
        private readonly int _throwAfterRows;
        private int _index = -1;

        public ThrowingDbDataReader(DataTable table, int throwAfterRows)
        {
            _table = table;
            _throwAfterRows = throwAfterRows;
        }

        public sealed class InjectedReadException : Exception
        {
            public InjectedReadException() : base("Injected source read failure.") { }
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (_index + 1 >= _throwAfterRows)
            {
                throw new InjectedReadException();
            }
            return Task.FromResult(Read());
        }

        public override bool Read()
        {
            _index++;
            return _index < _table.Rows.Count;
        }

        public override int FieldCount => _table.Columns.Count;
        public override object GetValue(int ordinal) => _table.Rows[_index][ordinal];
        public override int GetValues(object[] values)
        {
            object[] items = _table.Rows[_index].ItemArray;
            int n = Math.Min(values.Length, items.Length);
            Array.Copy(items, values, n);
            return n;
        }
        public override string GetName(int ordinal) => _table.Columns[ordinal].ColumnName;
        public override int GetOrdinal(string name) => _table.Columns[name].Ordinal;
        public override Type GetFieldType(int ordinal) => _table.Columns[ordinal].DataType;
        public override string GetDataTypeName(int ordinal) => _table.Columns[ordinal].DataType.Name;
        public override bool IsDBNull(int ordinal) => _table.Rows[_index][ordinal] == DBNull.Value;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => _table.Rows[_index][name];
        public override int Depth => 0;
        public override bool HasRows => _table.Rows.Count > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => _table.Rows.GetEnumerator();

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
    }

    // Verifies that an exception thrown from the source reader's ReadAsync during
    // an async bulk copy propagates as a faulted task through the continuation
    // chain (ReadFromRowSourceAsync -> CopyRowsAsync), and leaves the destination
    // connection usable.
    [Trait("Set", "2")]
    public class FaultedSourceReadAsync
    {
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup), nameof(DataTestUtility.IsNotAzureServer))]
        public void Test()
        {
#if DEBUG
            string dstConstr = DataTestUtility.TCPConnectionString;
            Task t = TestAsync(dstConstr);
            DataTestUtility.AssertThrowsInner<AggregateException, ThrowingDbDataReader.InjectedReadException>(() => t.Wait());
            Assert.True(t.IsCompleted, "Task did not complete! Status: " + t.Status);
#endif
        }

#if DEBUG
        private static async Task TestAsync(string dstConstr)
        {
            DataTable source = AsyncContinuationHelpers.BuildThreeColumnSource(200);

            using SqlConnection dstConn = new SqlConnection(dstConstr);
            dstConn.Open();

            using Table dstTable = new Table(
                dstConn,
                "BulkCopy_FaultedSourceReadAsync",
                AsyncContinuationHelpers.ThreeColumnTableDefinition);

            try
            {
                using ThrowingDbDataReader reader = new ThrowingDbDataReader(source, throwAfterRows: 40);
                using SqlBulkCopy bulkcopy = new SqlBulkCopy(dstConn);
                bulkcopy.DestinationTableName = dstTable.Name;
                bulkcopy.BatchSize = 25;
                bulkcopy.ColumnMappings.Add(0, "col1");
                bulkcopy.ColumnMappings.Add(1, "col2");
                bulkcopy.ColumnMappings.Add(2, "col3");

                using AsyncDebugScope debugScope = new AsyncDebugScope { ForceAllPends = true };

                await bulkcopy.WriteToServerAsync(reader);
            }
            finally
            {
                using SqlCommand ping = new SqlCommand("SELECT 1", dstConn);
                object result = ping.ExecuteScalar();
                DataTestUtility.AssertEqualsWithDescription(1, result, "Connection unusable after source read fault.");
            }
        }
#endif
    }
}
