this is a SQL orm that uses method signatures in order to access a database. users do not need to write an sql code, instead they can write only a method signature, and SigQL generates and materializes the results.

see IMonolithicRepository.cs for examples.

for every feature, please add integration tests for in SigQL.SqlServer.Tests and unit tests in SigQL.Tests