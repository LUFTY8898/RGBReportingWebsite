using Microsoft.Data.SqlClient;
using RGBReportingWebsite.Server.Models;

namespace RGBReportingWebsite.Server.Services
{
    public class ManufacturerService
    {
        private readonly string _connectionString;


        public ManufacturerService(
            IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("RGB_Reporting")
                ?? throw new InvalidOperationException(
                    "RGB_Reporting database connection string not found.");
        }


        // ============================================================
        // GET MANUFACTURERS
        //
        // OMRC:
        //
        // FirmCode = value used for filtering
        // FirmName = value displayed to the user
        // ============================================================

        public async Task<List<Manufacturer>>
            GetManufacturersAsync()
        {
            List<Manufacturer> manufacturers =
                new List<Manufacturer>();


            const string sql = @"
                SELECT
                    FirmCode,
                    FirmName
                FROM dbo.OMRC
                WHERE FirmName IS NOT NULL
                  AND LTRIM(RTRIM(FirmName)) <> ''
                ORDER BY
                    FirmName;";


            await using SqlConnection connection =
                new SqlConnection(
                    _connectionString);


            await connection.OpenAsync();


            await using SqlCommand command =
                new SqlCommand(
                    sql,
                    connection);


            command.CommandTimeout =
                3600;


            await using SqlDataReader reader =
                await command.ExecuteReaderAsync();


            while (await reader.ReadAsync())
            {
                Manufacturer manufacturer =
                    new Manufacturer
                    {
                        FirmCode =
                            Convert.ToInt32(
                                reader["FirmCode"]),

                        FirmName =
                            Convert.ToString(
                                reader["FirmName"])
                                ?.Trim()
                            ?? string.Empty
                    };


                manufacturers.Add(
                    manufacturer);
            }


            return manufacturers;
        }
    }
}