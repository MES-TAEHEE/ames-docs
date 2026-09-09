#:project ../src/01_Shared/AMES.Contracts/AMES.Contracts.csproj

using AMES.Contracts.Dto;

if (!PdaScenarioUsers.IsSimple("SCTEST1") || !PdaScenarioUsers.IsDetailed("SCTEST2"))
    throw new Exception("Current scenario accounts are not mapped correctly.");
if (!PdaScenarioUsers.IsSimple("TEST1") || !PdaScenarioUsers.IsDetailed("TEST"))
    throw new Exception("Legacy scenario accounts are not compatible.");
if (PdaScenarioUsers.IsSimple("SCTEST2") || PdaScenarioUsers.IsDetailed("SCTEST1"))
    throw new Exception("Simple and detailed scenario accounts overlap.");

Console.WriteLine("PASS: SCTEST1 simple mode; SCTEST2 detailed mode; legacy account compatibility.");
