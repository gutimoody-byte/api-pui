using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using PuiWebhookApi.Controllers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace PuiWebhookApi.Workers
{
    public class CoincidenciaWorker : BackgroundService
    {
        private readonly CoincidenciaRepository _repo;
        private readonly PuiAuthService _authService;
        private readonly TokenCache _tokenCache = new TokenCache();
        private readonly IConfiguration _config;

        public CoincidenciaWorker(CoincidenciaRepository repo, PuiAuthService authService, IConfiguration config)
        {
            _repo = repo;
            _authService = authService;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Console.WriteLine($"Worker ejecutado: {DateTime.Now}");

                    // 1. Buscar coincidencias con el SP
                    var coincidencias = _repo.ObtenerCoincidencias();

                    if (coincidencias.Any())
                    {
                        if (!_tokenCache.TokenValido)
                        {
                            // Generar token
                            var token_ = JwtHelper.GenerarToken(_config["Jwt:SecretKey"],
                                                                    _config["Jwt:Issuer"],
                                                                    _config["Jwt:Audience"]
                                                                );

                            var resultado = new
                            {
                                Exito = true,
                                Token = token_,
                                Mensaje = "JWT generado correctamente"
                            }; // await _authService.ObtenerTokenAsync(_config["Jwt:Audience"], "TU_CLAVE");  Esto es el bueno para generar el token.

                            if (resultado.Exito)
                            {
                                _tokenCache.GuardarToken(resultado.Token);
                                Console.WriteLine("Token renovado.");
                            }
                            else
                            {
                                Console.WriteLine($"Error al obtener token: {resultado.Mensaje}");
                                //continue; // saltar ciclo si no hay token válido
                            }
                        }

                        var token = _tokenCache.Token;
                        if (!string.IsNullOrEmpty(token))
                        {
                            foreach (var c in coincidencias)
                            {
                                var resultado = await _authService.NotificarCoincidenciaAsync(c, token);
                                Console.WriteLine(resultado.Mensaje);

                                if (resultado.Exito)
                                {
                                    _authService.ActualizarStatusCoincidencia(c.Folio);

                                    // Al terminar de notificar esa coincidencia, cerrar la búsqueda
                                    var cierre = await _authService.FinalizarBusquedaAsync(
                                        id: c.Folio, // el mismo folio
                                        institucionId: _config["Jwt:Audience"],
                                        token: token
                                    );

                                    Console.WriteLine(cierre.Mensaje);
                                    Console.WriteLine($"Coincidencia {c.Folio} marcada como Notificada.");

                                    if (cierre.Exito)
                                    {
                                        _authService.ActualizarBusquedaFinalizada(c.Folio);
                                    }
                                }
                                else
                                {
                                    //_repo.ActualizarStatusCoincidencia(c.Folio, false);
                                    Console.WriteLine($"Coincidencia {c.Folio} marcada con error.");
                                }
                            }
                        }
                    }

                    // 2. Esperar 15 minutos (para pruebas)
                    // await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);

                    // 2. Calcular espera dinámica hasta la próxima 1:00 AM
                    var ahora = DateTime.Now;
                    DateTime siguiente;

                    if (ahora.Hour < 1)
                    {
                        // Hoy a la 1 AM
                        siguiente = ahora.Date.AddHours(1);
                    }
                    else
                    {
                        // Mañana a la 1 AM
                        siguiente = ahora.Date.AddDays(1).AddHours(1);
                    }

                    var espera = siguiente - ahora;
                    // Seguridad: mínimo 1 minuto, máximo 24 horas
                    if (espera.TotalMinutes < 1)
                    {
                        espera = TimeSpan.FromMinutes(1);
                    }
                    else if (espera.TotalHours > 24)
                    {
                        espera = TimeSpan.FromHours(24);
                    }

                    Console.WriteLine($"⏸ Esperando hasta {siguiente}");
                    await Task.Delay(espera, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Captura global: evita que el worker muera
                    Console.WriteLine($"❌ Error inesperado en ciclo: {ex.Message}");

                    // Espera corta antes de reintentar para no ciclar
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }
    }

    public class TokenCache
    {
        private string _token;
        private DateTime _fechaObtencion;

        public bool TokenValido =>
            !string.IsNullOrEmpty(_token) && (DateTime.Now - _fechaObtencion).TotalMinutes < 48;

        public string Token => _token;

        public void GuardarToken(string token)
        {
            _token = token;
            _fechaObtencion = DateTime.Now;
        }
    }

    public class CoincidenciaRepository
    {
        private readonly string _connectionString;

        public CoincidenciaRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IEnumerable<CoincidenciaDto> ObtenerCoincidencias()
        {
            var lista = new List<CoincidenciaDto>();

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            using var cmd = new MySqlCommand("spSel_CoincidenciasPUI", conn);
            cmd.CommandType = CommandType.StoredProcedure;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var dto = new CoincidenciaDto
                {
                    Folio = reader["Folio"].ToString(),
                    Curp = reader["curp"].ToString(),
                    NombreCompleto = reader["nombre_completo"].ToString(),
                    Nombre = reader["nombre"].ToString(),
                    PrimerApellido = reader["primer_apellido"].ToString(),
                    SegundoApellido = reader["segundo_apellido"].ToString(),
                    FechaNacimiento = reader["fecha_nacimiento"].ToString(),
                    LugarNacimiento = reader["lugar_nacimiento"].ToString(),
                    SexoAsignado = reader["sexo_asignado"].ToString(),
                    Telefono = reader["telefono"].ToString(),
                    Correo = reader["correo"].ToString(),
                    Direccion = reader["direccion"].ToString(),
                    Calle = reader["calle"].ToString(),
                    Numero = reader["numero"].ToString(),
                    Colonia = reader["colonia"].ToString(),
                    CodigoPostal = reader["codigo_postal"].ToString(),
                    MunicipioOAlcaldia = reader["municipio_o_alcaldia"].ToString(),
                    EntidadFederativa = reader["entidad_federativa"].ToString()
                };

                lista.Add(dto);
            }

            return lista;
        }
    }

    public class CoincidenciaDto
    {
        public string Folio { get; set; }
        public string Curp { get; set; }
        public string NombreCompleto { get; set; }
        public string Nombre { get; set; }
        public string PrimerApellido { get; set; }
        public string SegundoApellido { get; set; }
        public string FechaNacimiento { get; set; }
        public string LugarNacimiento { get; set; }
        public string SexoAsignado { get; set; }
        public string Telefono { get; set; }
        public string Correo { get; set; }
        public string Direccion { get; set; }
        public string Calle { get; set; }
        public string Numero { get; set; }
        public string Colonia { get; set; }
        public string CodigoPostal { get; set; }
        public string MunicipioOAlcaldia { get; set; }
        public string EntidadFederativa { get; set; }
    }

    public class PuiAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public PuiAuthService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public async Task<(bool Exito, string Token, string Mensaje)> ObtenerTokenAsync(string institucionId, string clave)
        {
            var payload = new
            {
                institucion_id = institucionId,
                clave = clave
            };

            var response = await _httpClient.PostAsJsonAsync(
                "https://www.api.plataformadebusqueda.gob.mx/api/v2_3_0/login",
                payload);

            var contenido = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(contenido);
                var token = doc.RootElement.GetProperty("token").GetString();
                return (true, token, "JWT generado correctamente");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // 403 → Credenciales inválidas
                return (false, null, "Credenciales inválidas. Verifica institucion_id y clave.");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
            {
                // 500 → Error interno
                return (false, null, "Error procesando petición en el servidor PUI.");
            }
            else
            {
                // Otros códigos
                return (false, null, $"Error inesperado: {response.StatusCode} - {contenido}");
            }
        }

        public async Task<(bool Exito, string Mensaje)> NotificarCoincidenciaAsync(CoincidenciaDto c, string token)
        {
            var payload = new
            {
                curp = c.Curp,
                nombre_completo = new
                {
                    nombre = c.Nombre,
                    primer_apellido = c.PrimerApellido,
                    segundo_apellido = c.SegundoApellido
                },
                fecha_nacimiento = c.FechaNacimiento,
                lugar_nacimiento = c.LugarNacimiento,
                sexo_asignado = c.SexoAsignado,
                telefono = c.Telefono,
                correo = c.Correo,
                domicilio = new
                {
                    direccion = c.Direccion,
                    calle = c.Calle,
                    numero = c.Numero,
                    colonia = c.Colonia,
                    codigo_postal = c.CodigoPostal,
                    municipio_o_alcaldia = c.MunicipioOAlcaldia,
                    entidad_federativa = c.EntidadFederativa
                },
                id = $"{c.Folio}-{Guid.NewGuid()}",
                institucion_id = "TU_RFC_CON_HOMOCLAVE",
                fase_busqueda = "1" // según el manual: 1, 2 o 3
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync(
                "https://www.api.plataformadebusqueda.gob.mx/api/v2_3_0/notificar-coincidencia",
                payload);

            var contenido = await response.Content.ReadAsStringAsync();

            ////if (response.IsSuccessStatusCode)
            ////{
            ////    return (true, $"Coincidencia enviada correctamente: {contenido}");
            ////}
            ////else
            ////{
            ////    return (false, $"Error {response.StatusCode}: {contenido}");
            ////}

            return (true, $"Coincidencia enviada correctamente: {contenido}");
        }

        public void ActualizarStatusCoincidencia(string folio)
        {
            try
            {
                using var conn = new MySqlConnection(_config["ConnectionStrings:bdPrueba"]);
                conn.Open();

                using var cmd = new MySqlCommand(
                    "UPDATE PUI_TrazabilidadReporte " +
                    "SET NotificadoPUI = 1, FechaNotificacion = NOW() " +
                    "WHERE Folio = @folio",
                    conn);

                cmd.Parameters.AddWithValue("@folio", folio);

                int filas = cmd.ExecuteNonQuery();

                if (filas > 0)
                {
                    Console.WriteLine($"✅ Status actualizado para Folio {folio}");
                }
                else
                {
                    Console.WriteLine($"⚠️ No se encontró registro con Folio {folio}");
                }
            }
            catch (Exception ex)
            {
                // No detiene el worker, solo loguea
                Console.WriteLine($"❌ Error al actualizar status para Folio {folio}: {ex.Message}");
            }
        }


        public void ActualizarBusquedaFinalizada(string folio)
        {
            try
            {
                using var conn = new MySqlConnection(_config["ConnectionStrings:bdPrueba"]);
                conn.Open();

                using var cmd = new MySqlCommand(
                    "UPDATE PUI_TrazabilidadReporte " +
                    "SET BusquedaFinalizada = 1, FechaFinalizacion = NOW() " +
                    "WHERE Folio = @folio",
                    conn);

                cmd.Parameters.AddWithValue("@folio", folio);

                int filas = cmd.ExecuteNonQuery();

                if (filas > 0)
                {
                    Console.WriteLine($"✅ Búsqueda finalizada para Folio {folio}");
                }
                else
                {
                    Console.WriteLine($"⚠️ No se encontró registro con Folio {folio}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al marcar búsqueda finalizada para Folio {folio}: {ex.Message}");
            }
        }

        public async Task<(bool Exito, string Mensaje)> FinalizarBusquedaAsync(string id, string institucionId, string token)
        {
            try
            {
                var payload = new
                {
                    id = id,                 // El folio de tu tabla (FUB + UUID4)
                    institucion_id = institucionId // Tu RFC con homoclave
                };

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await client.PostAsJsonAsync(
                    "https://www.api.plataformadebusqueda.gob.mx/api/v2_3_0/busqueda-finalizada",
                    payload);

                var contenido = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return (true, $"✅ Búsqueda finalizada correctamente: {contenido}");
                }
                else
                {
                    return (false, $"❌ Error {response.StatusCode}: {contenido}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"❌ Excepción al finalizar búsqueda: {ex.Message}");
            }
        }

    }


}
