﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Simple.Data.Azure
{
    using System.ComponentModel.Composition;
    using System.Data;
    using Simple.Azure;
    using Simple.Azure.Helpers;

    [Export("Azure", typeof(Adapter))]
    public class AzureTableAdapter : Adapter
    {
        private const string PartitionKey = "PartitionKey";
        private const string RowKey = "RowKey";

        private AzureHelper _helper;

        protected override void OnSetup()
        {
            base.OnSetup();
            _helper = new AzureHelper { UrlBase = Settings.Url, SharedKey = Settings.Key, Account = Settings.Account };
        }

        public override IDictionary<string, object> GetKey(string tableName, IDictionary<string, object> record)
        {
            return new Dictionary<string, object>
                       {
                           { "PartitionKey", record["PartitionKey"]},
                           { "RowKey", record["RowKey"]}
                       };
        }

        public override IList<string> GetKeyNames(string tableName)
        {
            return new[] {"PartitionKey", "RowKey"};
        }

        public override IEnumerable<IDictionary<string, object>> Find(string tableName, SimpleExpression criteria)
        {
            var table = GetTable(tableName);
            if (ReferenceEquals(criteria, null)) return table.GetAllRows();

            var filter = new ExpressionFormatter().Format(criteria);
            return table.Query(filter);
        }

        private Table GetTable(string tableName)
        {
            return new Table(tableName, _helper);
        }

        public override IDictionary<string, object> Get(string tableName, params object[] keyValues)
        {
            if (keyValues.Length < 1 || keyValues.Length > 2)
            {
                throw new ArgumentException("AzureTableAdapter Get method requires PartitionKey and optional RowKey values.");
            }

            if (keyValues[0] == null) throw new ArgumentNullException("PartitionKey cannot be null.");

            var criteria = ObjectReference.FromStrings(tableName, PartitionKey) == keyValues[0].ToString();

            if (keyValues.Length == 2)
            {
                if (keyValues[1] == null) throw new ArgumentNullException("RowKey cannot be null.");
                criteria = criteria && ObjectReference.FromStrings(tableName, RowKey) == keyValues[1].ToString();
            }

            try
            {
                return Find(tableName, criteria).SingleOrDefault();
            }
            catch (InvalidOperationException)
            {
                throw new SimpleDataException("More than one row matched Get key criteria.");
            }
        }

        public override IEnumerable<IDictionary<string, object>> RunQuery(SimpleQuery query, out IEnumerable<SimpleQueryClauseBase> unhandledClauses)
        {
            unhandledClauses = query.Clauses.Where(c => !(c is WhereClause));
            var whereClauses = query.Clauses.OfType<WhereClause>().ToList();
            if (whereClauses.Count == 0)
            {
                return Find(query.TableName, null);
            }
            return Find(query.TableName,
                        query.Clauses.OfType<WhereClause>().DefaultIfEmpty().Select(w => w.Criteria).Aggregate((a, b) => a && b));
        }

        public override IDictionary<string, object> Insert(string tableName, IDictionary<string, object> data, bool resultRequired)
        {
            var table = GetTable(tableName);
            var row = table.InsertRow(data);
            return resultRequired ? row : null;
        }

        public override int Update(string tableName, IDictionary<string, object> data, SimpleExpression criteria)
        {
            var table = GetTable(tableName);

            return new UpdateHelper(this).Update(table, data, criteria);

        }

        //public override int Update(string tableName, IDictionary<string, object> data)
        //{
        //    var table = GetTable(tableName);
        //    table.UpdateRow(data);
        //    return 1;
        //}

        public override int Delete(string tableName, SimpleExpression criteria)
        {
            var table = GetTable(tableName);
            var keys = criteria.TryGetKeyCombo();
            if (keys != KeyCombo.Empty && !string.IsNullOrEmpty(keys.RowKey))
            {
                table.Delete(keys.PartitionKey, keys.RowKey);
                return 1;
            }

            var dict = criteria.ToDictionary();
            if (dict != null && dict.ContainsKey(PartitionKey) && dict.ContainsKey(RowKey))
            {
                table.Delete(dict[PartitionKey].ToStringOrEmpty(), dict[RowKey].ToStringOrEmpty());
                return 1;
            }

            int count = 0;
            foreach (var row in Find(tableName, criteria))
            {
                table.Delete(row);
                ++count;
            }
            return count;
        }

        public override bool IsExpressionFunction(string functionName, params object[] args)
        {
            throw new NotImplementedException();
        }
    }
}
