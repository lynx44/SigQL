using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SigQL.SqlServer.Tests
{
    internal class TestSettings
    {
        public static readonly string LaborConnectionString;
        public static readonly string LaborConnectionWithUserCredentials;
        public static IDbConnection LaborDbConnection => new SqlConnection(LaborConnectionString);
        public static IDbConnection LaborDbConnectionWithUserCredentials => new SqlConnection(LaborConnectionWithUserCredentials);

        static TestSettings()
        {
            string projectPath = AppDomain.CurrentDomain.BaseDirectory.Split(new String[] { @"bin\" }, StringSplitOptions.None)[0];
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(projectPath)
                .AddJsonFile("testSettings.json")
                .Build();
            LaborConnectionString = configuration.GetConnectionString("LaborConnection");
            LaborConnectionWithUserCredentials = configuration.GetConnectionString("LaborConnectionWithUserCredentials");
        }
    }
}
