using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PuiWebhookApi.Controllers
{
    //public class PuiWebhookController
    //{
    //}

    [ApiController]
    [Route("api/pui")]
    public class PuiWebhookController : ControllerBase
    {

        private readonly IConfiguration _config;
        private readonly SolicitudRepository _repo;
        public PuiWebhookController(IConfiguration config)
        {
            _config = config;
            _repo = new SolicitudRepository(_config);
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // Validar credenciales
            if (request.Usuario != "PUI" || request.Clave != _config["Jwt:ClavePui"])
            {
                return Unauthorized(new { error = "Credenciales inválidas" });
            }

            // Generar token
            var token = JwtHelper.GenerarToken(
                _config["Jwt:SecretKey"],
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"]
            );

            return Ok(new { token });
        }


        [HttpPost("activar-reporte")]
        [Authorize] // Protegido con JWT
        public async Task<IActionResult> ActivarReporte([FromBody] ActivarReporteRequest solicitud)
        {
            // 1. Validar campos obligatorios
            if (solicitud == null || string.IsNullOrEmpty(solicitud.Id) || string.IsNullOrEmpty(solicitud.Curp) || string.IsNullOrEmpty(solicitud.LugarNacimiento))
            {
                return BadRequest(new { message = "Payload inválido o campos obligatorios ausentes." });
            }

            if (solicitud.Curp.Length != 18)
            {
                return BadRequest(new { message = "CURP inválida, debe tener 18 caracteres." });
            }

            // 2. Procesar búsqueda en DB (ejemplo: coincidencia)
            // PuiResultadoBusqueda resultado = _repo.BuscarCoincidenciaReporte(solicitud);

            // 3. Guardar trazabilidad
            
            string jsonEntrada = System.Text.Json.JsonSerializer.Serialize(solicitud);
            string jsonRespuesta = System.Text.Json.JsonSerializer.Serialize(new { message = "En Proceso." }); //resultado

            _repo.InsertarTrazabilidadReporte(
                solicitud,
                //resultado.Mensaje,
                "En proceso",
                "Procesado en ActivarReporte",
                jsonEntrada,
                jsonRespuesta
            );

            // 4. Responder éxito
            return Ok(new { message = "La solicitud de activación del reporte de búsqueda se recibió correctamente." });
        }

        //public async Task<IActionResult> RecibirSolicitud([FromBody] PuiSolicitud solicitud)
        //{
        //    if (solicitud == null || string.IsNullOrEmpty(solicitud.Folio))
        //    {
        //        return BadRequest(new { mensaje = "Payload inválido o Folio ausente." });
        //    }

        //    //string token_pui = await ObtenerTokenAsync();

        //    var repo = new SolicitudRepository(_config);

        //    // 1. Ejecutar la búsqueda en MySQL (Devuelve el DTO estructurado)
        //    PuiResultadoBusqueda resultado = repo.BuscarCoincidencia(solicitud);

        //    // 2. Guardar la trazabilidad de forma segura convirtiendo a JSON estructurado
        //    string jsonEntrada = System.Text.Json.JsonSerializer.Serialize(solicitud);
        //    string jsonRespuesta = System.Text.Json.JsonSerializer.Serialize(resultado);

        //    repo.InsertarTrazabilidad(
        //        solicitud,
        //        resultado.Mensaje,
        //        "Procesado en Webhook síncono",
        //        jsonEntrada,
        //        jsonRespuesta
        //    );

        //    // 3. Responder DIRECTAMENTE al gobierno en la misma petición HTTP
        //    // Si hubo coincidencia va con datos; si no, va vacío pero con su folio de rastreo.
        //    return Ok(resultado);
        //}

        public async Task<string> ObtenerTokenAsync()
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://www.api.plataformadebusqueda.gob.mx");

            var payload = new
            {
                institucion_id = _config["Jwt:Audience"],
                clave = ""
            };

            var response = await client.PostAsJsonAsync("/api/2_3_0/login3", payload);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                return json["token"]; // JWT válido
            }
            else
            {
                throw new Exception($"Error al obtener token: {response.StatusCode}");
            }
        }

        // Endpoint de prueba
        [HttpPost("activar-reporte-prueba")]
        [Authorize] // Protegido con JWT
        public async Task<IActionResult> ActivarReportePrueba([FromBody] ActivarReporteRequest solicitud)
        {
            // 1. Validar payload
            if (solicitud == null || string.IsNullOrEmpty(solicitud.Id) || string.IsNullOrEmpty(solicitud.Curp))
            {
                return BadRequest(new { mensaje = "Payload inválido o campos obligatorios ausentes." });
            }

            // 2. Simular búsqueda en DB (dummy)
            var resultado = new PuiResultadoBusqueda_p
            {
                Folio = solicitud.Id,
                Mensaje = "Prueba recibida correctamente",
                Coincidencia = true
            };

            // 3. Guardar trazabilidad
            string jsonEntrada = JsonSerializer.Serialize(solicitud);
            string jsonRespuesta = JsonSerializer.Serialize(resultado);

            _repo.InsertarTrazabilidadReporte(
                solicitud,
                resultado.Mensaje,
                "Procesado en Webhook de prueba",
                jsonEntrada,
                jsonRespuesta
            );

            // 4. Responder con 200 OK y el resultado
            return Ok(resultado);
        }


        [HttpPost("desactivar-reporte")]
        [Authorize] // Protegido con JWT
        public IActionResult DesactivarReporte([FromBody] DesactivarReporteRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Id))
            {
                return BadRequest(new { message = "Payload inválido o Id ausente." });
            }

            // 1. Dar de baja el caso en DB
            _repo.DesactivarCaso(request.Id);

            // 2. Guardar trazabilidad
            //string jsonEntrada = System.Text.Json.JsonSerializer.Serialize(request);
            //_repo.InsertarTrazabilidadDesactivacion(
            //    request.Id,
            //    "Caso desactivado",
            //    jsonEntrada
            //);

            // 3. Responder al PUI
            return Ok(new { message = "Registro de finalización de búsqueda histórica guardado correctamente" });
        }


    }
    public class DesactivarReporteRequest
    {
        public string Id { get; set; }
    }
    public class ActivarReporteRequest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }                // obligatorio

        [JsonPropertyName("curp")]
        public string Curp { get; set; }              // obligatorio

        [JsonPropertyName("nombre")]
        public string Nombre { get; set; }

        [JsonPropertyName("primer_apellido")]
        public string PrimerApellido { get; set; }

        [JsonPropertyName("segundo_apellido")]
        public string SegundoApellido { get; set; }

        [JsonPropertyName("fecha_nacimiento")]
        public string FechaNacimiento { get; set; }   // YYYY-MM-DD

        [JsonPropertyName("fecha_desaparicion")]
        public string FechaDesaparicion { get; set; } // YYYY-MM-DD

        [JsonPropertyName("lugar_nacimiento")]
        public string LugarNacimiento { get; set; }   // obligatorio

        [JsonPropertyName("sexo_asignado")]
        public string SexoAsignado { get; set; }      // H/M/X

        [JsonPropertyName("telefono")]
        public string Telefono { get; set; }

        [JsonPropertyName("correo")]
        public string Correo { get; set; }

        [JsonPropertyName("direccion")]
        public string Direccion { get; set; }

        [JsonPropertyName("calle")]
        public string Calle { get; set; }

        [JsonPropertyName("numero")]
        public string Numero { get; set; }

        [JsonPropertyName("colonia")]
        public string Colonia { get; set; }

        [JsonPropertyName("codigo_postal")]
        public string CodigoPostal { get; set; }

        [JsonPropertyName("municipio_o_alcaldia")]
        public string MunicipioOAlcaldia { get; set; }

        [JsonPropertyName("entidad_federativa")]
        public string EntidadFederativa { get; set; }
    }
    public class PuiSolicitud
    {
        public string Folio { get; set; }
        public string Curp { get; set; }
        public string Nombre { get; set; }
        public string ApellidoPaterno { get; set; }
        public string ApellidoMaterno { get; set; }
        public DateTime? FechaNacimiento { get; set; }
    }
    public class LoginRequest
    {
        public string Usuario { get; set; }
        public string Clave { get; set; }
    }
    public class PuiResultadoBusqueda
    {
        [JsonPropertyName("mensaje")]
        public string Mensaje { get; set; }

        [JsonPropertyName("folio")]
        public string Folio { get; set; }

        // Fuerza a que en el JSON final se renderice con guion bajo
        [JsonPropertyName("coincidencia_encontrada")]
        public bool CoincidenciaEncontrada { get; set; }

        [JsonPropertyName("datos_localizados")]
        public DatosPersona DatosLocalizados { get; set; }
    }
    // DTOs
    public class PuiSolicitud_p
    {
        public string Id { get; set; }
        public string Curp { get; set; }
        public string Nombre { get; set; }
        public string Primer_Apellido { get; set; }
        public string Segundo_Apellido { get; set; }
        public DateTime? Fecha_Nacimiento { get; set; }
        public DateTime? Fecha_Desaparicion { get; set; }
        public string Lugar_Nacimiento { get; set; }
        public string Sexo_Asignado { get; set; }
        public string Telefono { get; set; }
        public string Correo { get; set; }
        public string Direccion { get; set; }
        public string Calle { get; set; }
        public string Numero { get; set; }
        public string Colonia { get; set; }
        public string Codigo_Postal { get; set; }
        public string Municipio_O_Alcaldia { get; set; }
        public string Entidad_Federativa { get; set; }
    }
    public class PuiResultadoBusqueda_p
    {
        public string Folio { get; set; }
        public string Mensaje { get; set; }
        public bool Coincidencia { get; set; }
    }
    public class DatosPersona
    {
        [JsonPropertyName("curp")]
        public string Curp { get; set; }

        [JsonPropertyName("nombre")]
        public string Nombre { get; set; }

        [JsonPropertyName("apellido_paterno")] // Corregido a snake_case
        public string ApellidoPaterno { get; set; }

        [JsonPropertyName("apellido_materno")] // Corregido a snake_case
        public string ApellidoMaterno { get; set; }

        [JsonPropertyName("fecha_nacimiento")] // Corregido a snake_case
        public string FechaNacimiento { get; set; }
    }
    public class SolicitudRepository
    {
        private readonly string _connectionString;

        public SolicitudRepository(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("bdPrueba");
        }

        public PuiResultadoBusqueda BuscarCoincidenciaReporte(ActivarReporteRequest solicitud)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                // Fase 1: Buscar por CURP
                string sqlCurp = @"SELECT CURP, Nombres, ApellidoPaterno, ApellidoMaterno, FechaNacimiento 
                           FROM solicitante 
                           WHERE CURP = @Curp LIMIT 1";

                using (var cmd = new MySqlCommand(sqlCurp, conn))
                {
                    cmd.Parameters.AddWithValue("@Curp", solicitud.Curp);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            DateTime fechaDb = Convert.ToDateTime(reader["FechaNacimiento"]);

                            return new PuiResultadoBusqueda
                            {
                                Mensaje = "Coincidencia encontrada por CURP",
                                Folio = solicitud.Id, // aquí usamos el Id del reporte
                                CoincidenciaEncontrada = true,
                                DatosLocalizados = new DatosPersona
                                {
                                    Curp = reader["CURP"].ToString(),
                                    Nombre = reader["Nombres"].ToString(),
                                    ApellidoPaterno = reader["ApellidoPaterno"].ToString(),
                                    ApellidoMaterno = reader["ApellidoMaterno"].ToString(),
                                    FechaNacimiento = fechaDb.ToString("yyyy-MM-dd")
                                }
                            };
                        }
                    }
                }

                // Fase 2: Buscar por Nombre + Apellidos
                //string sqlNombre = @"SELECT CURP, Nombres, ApellidoPaterno, ApellidoMaterno, FechaNacimiento 
                //             FROM solicitante 
                //             WHERE Nombres = @Nombre 
                //               AND ApellidoPaterno = @ApellidoPaterno 
                //               AND ApellidoMaterno = @ApellidoMaterno 
                //             LIMIT 1";

                //using (var cmd = new MySqlCommand(sqlNombre, conn))
                //{
                //    cmd.Parameters.AddWithValue("@Nombre", solicitud.Nombre ?? (object)DBNull.Value);
                //    cmd.Parameters.AddWithValue("@ApellidoPaterno", solicitud.Primer_Apellido ?? (object)DBNull.Value);
                //    cmd.Parameters.AddWithValue("@ApellidoMaterno", solicitud.Segundo_Apellido ?? (object)DBNull.Value);

                //    using (var reader = cmd.ExecuteReader())
                //    {
                //        if (reader.Read())
                //        {
                //            DateTime fechaDb = Convert.ToDateTime(reader["FechaNacimiento"]);

                //            return new PuiResultadoBusqueda
                //            {
                //                Mensaje = "Coincidencia encontrada por Nombre/Apellidos",
                //                Folio = solicitud.Id,
                //                CoincidenciaEncontrada = true,
                //                DatosLocalizados = new DatosPersona
                //                {
                //                    Curp = reader["CURP"].ToString(),
                //                    Nombre = reader["Nombres"].ToString(),
                //                    ApellidoPaterno = reader["ApellidoPaterno"].ToString(),
                //                    ApellidoMaterno = reader["ApellidoMaterno"].ToString(),
                //                    FechaNacimiento = fechaDb.ToString("yyyy-MM-dd")
                //                }
                //            };
                //        }
                //    }
                //}

            }

            // Si no hay coincidencia en ninguna fase
            return new PuiResultadoBusqueda
            {
                Mensaje = "Sin coincidencia",
                Folio = solicitud.Id,
                CoincidenciaEncontrada = false,
                DatosLocalizados = null
            };
        }

        public PuiResultadoBusqueda BuscarCoincidencia(PuiSolicitud solicitud)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                // NOTA: Recuerda que el manual exige buscar por fases. 
                // Si aquí no encuentra por CURP, deberías meter un IF y buscar por Nombre/Apellidos (Fase 3)
                string sql = @"SELECT Curp, Nombres, ApellidoPaterno, ApellidoMaterno, FechaNacimiento 
                       FROM solicitante 
                       WHERE Curp = @Curp LIMIT 1";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Curp", solicitud.Curp);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            DateTime fechaDb = Convert.ToDateTime(reader["FechaNacimiento"]);

                            return new PuiResultadoBusqueda
                            {
                                Mensaje = "Coincidencia encontrada",
                                Folio = solicitud.Folio,
                                CoincidenciaEncontrada = true,
                                DatosLocalizados = new DatosPersona
                                {
                                    Curp = reader["Curp"].ToString(),
                                    Nombre = reader["Nombres"].ToString(),
                                    ApellidoPaterno = reader["ApellidoPaterno"].ToString(),
                                    ApellidoMaterno = reader["ApellidoMaterno"].ToString(),
                                    FechaNacimiento = fechaDb.ToString("yyyy-MM-dd")
                                }
                            };
                        }
                    }
                }
            }

            // Si no hay coincidencia, devolvemos la estructura limpia exigida por el manual
            return new PuiResultadoBusqueda
            {
                Mensaje = "Sin coincidencia",
                Folio = solicitud.Folio,
                CoincidenciaEncontrada = false,
                DatosLocalizados = null
            };
        }

        public void InsertarTrazabilidad(PuiSolicitud solicitud, string resultado, string mensaje, string jsonEntrada, string jsonRespuesta)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"INSERT INTO PUI_TrazabilidadWebhook 
                           (Folio, Curp, Nombre, ApellidoPaterno, ApellidoMaterno, FechaNacimiento, Resultado, Mensaje, JsonEntrada, JsonRespuesta) 
                           VALUES (@Folio, @Curp, @Nombre, @ApellidoPaterno, @ApellidoMaterno, @FechaNacimiento, @Resultado, @Mensaje, @JsonEntrada, @JsonRespuesta)";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Folio", solicitud.Folio);
                    cmd.Parameters.AddWithValue("@Curp", solicitud.Curp ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Nombre", solicitud.Nombre ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ApellidoPaterno", solicitud.ApellidoPaterno ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ApellidoMaterno", solicitud.ApellidoMaterno ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FechaNacimiento", solicitud.FechaNacimiento == default ? (object)DBNull.Value : solicitud.FechaNacimiento);
                    cmd.Parameters.AddWithValue("@Resultado", resultado);
                    cmd.Parameters.AddWithValue("@Mensaje", mensaje ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@JsonEntrada", jsonEntrada ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@JsonRespuesta", jsonRespuesta ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void InsertarTrazabilidad_p(PuiSolicitud_p solicitud, string resultado, string mensaje, string jsonEntrada, string jsonRespuesta)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"INSERT INTO PUI_TrazabilidadWebhook 
                           (Folio, Curp, Nombre, ApellidoPaterno, ApellidoMaterno, FechaNacimiento, Resultado, Mensaje, JsonEntrada, JsonRespuesta) 
                           VALUES (@Folio, @Curp, @Nombre, @ApellidoPaterno, @ApellidoMaterno, @FechaNacimiento, @Resultado, @Mensaje, @JsonEntrada, @JsonRespuesta)";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Folio", solicitud.Id);
                    cmd.Parameters.AddWithValue("@Curp", solicitud.Curp ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Nombre", solicitud.Nombre ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ApellidoPaterno", solicitud.Primer_Apellido ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ApellidoMaterno", solicitud.Segundo_Apellido ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FechaNacimiento", solicitud.Fecha_Nacimiento == default ? (object)DBNull.Value : solicitud.Fecha_Nacimiento);
                    cmd.Parameters.AddWithValue("@Resultado", resultado);
                    cmd.Parameters.AddWithValue("@Mensaje", mensaje ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@JsonEntrada", jsonEntrada ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@JsonRespuesta", jsonRespuesta ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void InsertarTrazabilidadReporte(ActivarReporteRequest solicitud, string resultado, string mensaje, string jsonEntrada, string jsonRespuesta)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"INSERT INTO PUI_TrazabilidadReporte 
                       (Folio, Curp, Nombre, Primer_Apellido, Segundo_Apellido, Fecha_Nacimiento, Fecha_Desaparicion, Lugar_Nacimiento, Sexo_Asignado, Telefono, Correo, Direccion, Calle, Numero, Colonia, Codigo_Postal, Municipio_O_Alcaldia, Entidad_Federativa, Resultado, Mensaje, JsonEntrada, JsonRespuesta) 
                       VALUES (@Folio, @Curp, @Nombre, @Primer_Apellido, @Segundo_Apellido, @Fecha_Nacimiento, @Fecha_Desaparicion, @Lugar_Nacimiento, @Sexo_Asignado, @Telefono, @Correo, @Direccion, @Calle, @Numero, @Colonia, @Codigo_Postal, @Municipio_O_Alcaldia, @Entidad_Federativa, @Resultado, @Mensaje, @JsonEntrada, @JsonRespuesta)";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Folio", solicitud.Id);
                    cmd.Parameters.AddWithValue("@Curp", solicitud.Curp ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Nombre", solicitud.Nombre ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Primer_Apellido", solicitud.PrimerApellido ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Segundo_Apellido", solicitud.SegundoApellido ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Fecha_Nacimiento", solicitud.FechaNacimiento ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Fecha_Desaparicion", solicitud.FechaDesaparicion ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Lugar_Nacimiento", solicitud.LugarNacimiento ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Sexo_Asignado", solicitud.SexoAsignado ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Telefono", solicitud.Telefono ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Correo", solicitud.Correo ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Direccion", solicitud.Direccion ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Calle", solicitud.Calle ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Numero", solicitud.Numero ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Colonia", solicitud.Colonia ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Codigo_Postal", solicitud.CodigoPostal ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Municipio_O_Alcaldia", solicitud.MunicipioOAlcaldia ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Entidad_Federativa", solicitud.EntidadFederativa ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Resultado", resultado);
                    cmd.Parameters.AddWithValue("@Mensaje", mensaje ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@JsonEntrada", jsonEntrada ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@JsonRespuesta", jsonRespuesta ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }

        }

        public void DesactivarCaso(string id)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"UPDATE PUI_TrazabilidadReporte 
                       SET Activo = 0, FechaDesactivacion = NOW() 
                       WHERE Folio = @Id";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void InsertarTrazabilidadDesactivacion(string id, string mensaje, string jsonEntrada)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"INSERT INTO PUI_TrazabilidadReporte 
                       (Folio, Resultado, Mensaje, JsonEntrada, Activo, FechaDesactivacion) 
                       VALUES (@Folio, 'Desactivado', @Mensaje, @JsonEntrada, 0, NOW())";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Folio", id);
                    cmd.Parameters.AddWithValue("@Mensaje", mensaje);
                    cmd.Parameters.AddWithValue("@JsonEntrada", jsonEntrada);
                    cmd.ExecuteNonQuery();
                }
            }
        }

    }
}
