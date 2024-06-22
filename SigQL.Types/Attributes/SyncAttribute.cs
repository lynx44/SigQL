using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Types.Attributes
{
    public class SyncAttribute : Attribute, IUpsertAttribute
    {
        public string TableName { get; set; }
    }
}
