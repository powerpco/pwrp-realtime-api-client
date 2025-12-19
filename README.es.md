# PowerP API Client: Guia Rapida

## Descripcion
Cliente de ejemplo para la API en tiempo real de PowerP. Incluye una biblioteca reutilizable en .NET 8 y ejemplos en C# y Python que muestran como listar senales y consultar valores en bloques pequenos para evitar congestionar el servicio.

## Estructura del repositorio
- `src/PowerP.Realtime.API.Client/`: biblioteca .NET con `PowerPAPIClient` y DTOs.
- `samples/csharp/`: consola de ejemplo con consultas por bloques (max 20 senales).
- `samples/python/`: notebook de referencia (`PowerPAPIClient.ipynb`) y requisitos de Python.
- `PowerP.Realtime.API.Client.sln`: solucion que incluye la biblioteca y el sample.

## Variables de entorno
- `POWERP_API_KEY`: token Bearer (obligatorio).
- `POWERP_API_BASE_URL`: URL base del tenant, ej. `https://tenant.powerp.app/rt-api/api/` (opcional).

Configura por shell:
- Bash: `export POWERP_API_KEY=tu_token` y `export POWERP_API_BASE_URL=https://tenant.powerp.app/rt-api/api/`
- PowerShell: `$env:POWERP_API_KEY='tu_token'` y `$env:POWERP_API_BASE_URL='https://tenant.powerp.app/rt-api/api/'`

## Comandos basicos
- Restaurar y compilar: `dotnet restore PowerP.Realtime.API.Client.sln` y `dotnet build PowerP.Realtime.API.Client.sln`.
- Ejecutar sample C#: `dotnet run --project samples/csharp/PowerP.Realtime.API.Sample.csproj`.
- Instalar Python: `pip install -r samples/python/requirements.txt` y abrir `samples/python/PowerPAPIClient.ipynb` en Jupyter.
- Formato C#: `dotnet format` (si esta disponible).

## Buenas practicas de consumo
- Bloques entre 5 y 10 senales; nunca mas de 20 (limite de API).
- Ventanas de raw data menores a 30 minutos; el sample usa 15 minutos.
- Reutiliza `HttpClient` y valida respuestas con `EnsureSuccessStatusCode`.
- No registres tokens en logs ni compartas payloads completos.
- Ante 429/5xx aplica backoff y reduce tamanos de bloque.

