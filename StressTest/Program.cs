using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Provisioning.Service;
using Microsoft.Azure.Devices.Shared;

namespace StressTest
{
    class Program

    {
        private static string s_idScope = "_REPLACE_ME_"; // example: 0ne004B844D
        private static string s_primaryKey = "_REPLACE_ME_";
        private static string s_secondaryKey = "_REPLACE_ME_";
        private static string s_registrationId = "_REPLACE_ME_"; // as printed by dpscmd (for test environments)
        private static string s_globalEndpoint = "_REPLACE_ME_"; //  <<dpsname>>.azure-devices-provisioning.net";

        public static ProvisioningServiceClient _provisioningServiceClient = null;
        public static ProvisioningDeviceClient _provisioningDeviceClient = null;

        // Request statistics for logging purposes
        public static int successCount = 0;
        public static int error409Count = 0;
        public static int error429Count = 0;
        public static int attemptedCount = 0;
        public static int completedCount = 0;
        public static int inflightCount = 0;

        public static object s_lock = new object();

        public static void Increment(ref int number, int delta)
        {
            lock(s_lock)
            {
                number += delta;
            }
        }

        public static async Task RegisterAsync(bool shouldLog)
        {
            try
            {
                Increment(ref inflightCount, 1);
                Increment(ref attemptedCount, 1);

                var result = await RegisterAsync();
                Increment(ref successCount, 1);

                if (!shouldLog)
                    return;

                Console.WriteLine(result.AssignedHub);
                Console.WriteLine(result.CreatedDateTimeUtc);
                Console.WriteLine(result.DeviceId);
                Console.WriteLine(result.ErrorCode);
                Console.WriteLine(result.ErrorMessage);
                Console.WriteLine(result.Etag);
                Console.WriteLine(result.GenerationId);
                Console.WriteLine(result.JsonPayload);
                Console.WriteLine(result.LastUpdatedDateTimeUtc);
                Console.WriteLine(result.RegistrationId);
                Console.WriteLine(result.Status);
                Console.WriteLine(result.Substatus);
                Console.WriteLine(result.ToString());
            }
            catch(ProvisioningTransportException pex)
            {
                if (pex.Message.Contains("429") || pex.Message.Contains("Precondition"))
                {
                    Console.WriteLine("429");
                    Increment(ref error429Count, 1);
                    return;
                }

                if (!pex.Message.Contains("Precondition"))
                {
                    Console.WriteLine(pex.Message);
                }
                
                // await Task.Delay(1000);
            }
            catch(Exception ex)
            {
                if (ex.Message.Contains("409"))
                {
                    Console.WriteLine("409");
                    Increment(ref error409Count, 1);
                    return;
                }

                if (ex.Message.Contains("429"))
                {
                    Console.WriteLine("429");
                    Increment(ref error429Count, 1);
                    return;
                }
                Console.WriteLine(ex.Message);
            }
            finally
            {
                Increment(ref completedCount, 1);
                Increment(ref inflightCount, -1);
            }
        }

        public static DateTime s_lastTime = DateTime.Now.AddDays(-1);

        public static void Log()
        {
            var s = $"inflight: {inflightCount}, attempts: {attemptedCount}, completed: {completedCount}, success count: {successCount}, 409 count: {error409Count}, 429 count: {error429Count}";
            Console.WriteLine(s);

            if (DateTime.Now - s_lastTime > TimeSpan.FromSeconds(10))
            {
                try
                {
                    File.AppendAllText(@"c:\\home\\log.txt", System.Environment.ProcessId + " - " + s + "\n");
                }
                catch
                {
                }
            }
        }

        public static async Task Main(string[] args)
        {
            Initialize();

            // await CreateEnrollmentGroupAsync();
            // await QueryEnrollmentGroupAsync();

            // await RegisterAsync(true);

            int n = -1;

            //for (int i = 0; i < n; i++)
            //{
            //    Log();
            //    _ = RegisterAsync(false);
            //    // await Task.Delay(10);
            //}

            while (true)
            {
                if (File.Exists(@"C:\\home\\stop.txt"))
                {
                    Console.WriteLine("EXITING...");
                    System.Environment.Exit(0);
                }

                //if (completedCount > 1)
                //{
                //    Log();
                //    Console.WriteLine("FORCE EXITING");
                //    System.Environment.Exit(0);
                //}

                int inFlightThreadsTarget = 1;

                while (true)
                {
                    try
                    {
                        var numString = File.ReadAllText(@"C:\\home\\num.txt");
                        inFlightThreadsTarget = int.Parse(numString);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(100);
                    }

                    Console.WriteLine("LOOPING");
                }

                if (inflightCount < inFlightThreadsTarget)
                {
                    _ = RegisterAsync(false);
                }

                await Task.Delay(10);
                Log();
            }

            Console.WriteLine("Exiting...");
            
            while (completedCount < n)
            {
                Log();
                await Task.Delay(1000);
            }

            //await RegisterAsync(true);

            Log();
            Console.WriteLine("DONE");
        }

        public static async Task<DeviceRegistrationResult> RegisterAsync()
        {
            using var security = new SecurityProviderSymmetricKey(
                s_registrationId,
                s_primaryKey,
                s_secondaryKey);
            _provisioningDeviceClient = ProvisioningDeviceClient.Create(s_globalEndpoint, s_idScope, security, new ProvisioningTransportHandlerMqtt());

            var result = await _provisioningDeviceClient.RegisterAsync();
            return result;
            // Console.WriteLine(result);
        }

        public static void Initialize()
        {
            // _provisioningServiceClient = ProvisioningServiceClient.CreateFromConnectionString(s_connectionString);
            //_deviceClient = DeviceClient.CreateFromConnectionString(s_connectionString);
            //_deviceClient.reg

            // Bypass https cert validation so we can use fiddler
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
            //ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.MaxServicePointIdleTime = 0;
            ServicePointManager.SetTcpKeepAlive(false, 0, 0);
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, errors) =>
            {
                return true;
            };
        }

        public static async Task CreateEnrollmentGroupAsync()
        {
            /*
            Console.WriteLine("\nCreating a new enrollmentGroup...");
            // Attestation attestation = X509Attestation.CreateFromRootCertificates(_groupIssuerCertificate);
            var attestation = new SymmetricKeyAttestation(s_primaryKey, s_secondaryKey);
            EnrollmentGroup enrollmentGroup =
                    new EnrollmentGroup(
                            s_enrollmentGroup,
                            attestation);
            Console.WriteLine(enrollmentGroup);

            Console.WriteLine("\nAdding new enrollmentGroup...");
            EnrollmentGroup enrollmentGroupResult =
                await _provisioningServiceClient.CreateOrUpdateEnrollmentGroupAsync(enrollmentGroup).ConfigureAwait(false);
            Console.WriteLine("\nEnrollmentGroup created with success.");
            Console.WriteLine(enrollmentGroupResult);
            */
        }

        public static async Task QueryEnrollmentGroupAsync()
        {
            Console.WriteLine("\nCreating a query for enrollmentGroups...");
            QuerySpecification querySpecification = new QuerySpecification("SELECT * FROM enrollmentGroups");
            using (Query query = _provisioningServiceClient.CreateEnrollmentGroupQuery(querySpecification))
            {
                while (query.HasNext())
                {
                    Console.WriteLine("\nQuerying the next enrollmentGroups...");
                    QueryResult queryResult = await query.NextAsync().ConfigureAwait(false);
                    Console.WriteLine(queryResult);

                    foreach (EnrollmentGroup group in queryResult.Items)
                    {
                        Console.WriteLine("");
                        // TODO: await EnumerateRegistrationsInGroup(querySpecification, group).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
