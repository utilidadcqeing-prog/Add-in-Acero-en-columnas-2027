# CQEING Cuadro de Cantidades

Addin para **Autodesk Revit 2026** diseñado en **C# (.NET 8)** y **WPF**. Utiliza **Entity Framework Core en memoria (InMemory)** para el almacenamiento local transitorio de datos y sigue el patrón de diseño **MVVM** (Model-View-ViewModel).

La aplicación permite visualizar y gestionar un cuadro de cantidades y presupuestos directamente desde Revit a través de una ventana modeless (flotante/no-bloqueante).

---

## 📋 Requisitos del Sistema y Entorno

Para poder compilar y ejecutar este addin correctamente, se requiere la siguiente configuración:

### 1. Requisitos de Software

* **Sistema Operativo**: Windows 10 o Windows 11 (64-bit).
* **Entorno de Compilación**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) o superior instalado en el equipo de desarrollo.
* **Plataforma BIM**: [Autodesk Revit 2026](https://www.autodesk.com/products/revit/overview) instalado.
  * Por defecto, el archivo del proyecto busca la instalación en `C:\Program Files\Autodesk\Revit 2026`.
  * Si tu instalación está en otra ubicación, puedes modificar la variable `<RevitInstallDir>` en el archivo [CQEING_CuadroCantidades.csproj](file:///d:/Addins/CQEING_CuadroCantidades/CQEING_CuadroCantidades.csproj).

---

## 📦 Dependencias y Librerías

El proyecto hace uso de dependencias específicas para interactuar con Revit, así como de librerías del ecosistema de .NET para la base de datos y la arquitectura interna.

### Librerías de Revit (Referencias Externas)

Estas dependencias se cargan directamente desde la instalación local de Revit (no se copian a la carpeta de salida para evitar conflictos de versiones):

* **RevitAPI.dll**: API interna de modelado de Revit.
* **RevitAPIUI.dll**: API para interactuar con la interfaz gráfica de Revit.

### Paquetes NuGet

El proyecto gestiona de forma automática los siguientes paquetes NuGet durante el proceso de compilación:

* **CommunityToolkit.Mvvm** (v8.4.2): Implementa comandos, notificaciones de propiedades y mensajería del patrón MVVM de forma eficiente y optimizada.
* **Microsoft.EntityFrameworkCore.InMemory** (v8.0.10): Proveedor de base de datos en memoria para Entity Framework Core.
* **Microsoft.Extensions.DependencyInjection** (v8.0.1): Contenedor de Inyección de Dependencias (DI) usado para registrar servicios, ViewModels y repositorios.
* **Microsoft.EntityFrameworkCore.Design** (v8.0.10): Herramientas de diseño y soporte para migraciones de EF Core.

---

## 🗄️ Persistencia y Almacenamiento en Memoria

El addin maneja un almacenamiento en memoria transitorio para gestionar la sesión actual del usuario de forma rápida y desacoplada de la base de datos física del servidor web.

* **Comportamiento dinámico**:
  * **Con documento activo en Revit**: Crea o abre un almacén en memoria específico para el archivo de Revit actual con el formato `MemoryCache_<NombreProyecto>`. Esto permite mantener los presupuestos y cantidades asociados a cada proyecto Revit de forma independiente.
  * **Sin documento activo / Global**: Si se inicializa sin un proyecto abierto, se guardará en `MemoryCache_Global`.
* **Inicialización automática**: Al hacer clic en el botón de la pestaña de Revit, el servicio de base de datos ejecuta `EnsureCreatedAsync()`, lo que crea el almacén en memoria de forma automática si no existe.

---

## 🛠️ Instrucciones de Compilación y Despliegue

La compilación y registro en Revit se han automatizado mediante tareas de postcompilación en MSBuild.

### Compilar el proyecto

Abre una terminal de PowerShell o CMD en la raíz del proyecto y ejecuta:

```powershell
dotnet build
```

### ¿Qué ocurre en segundo plano durante la compilación?

Cuando ejecutas el comando anterior, suceden dos cosas automáticamente:

1. **Compilación de binarios**: Se generan las DLLs del addin en `bin\Debug\net8.0-windows\` o `bin\Release\net8.0-windows\`.
2. **Registro de Manifiesto en Revit**: Se genera el archivo descriptor `CQEING_CuadroCantidades.addin` en la carpeta de extensiones de Revit:

   ```text
   %APPDATA%\Autodesk\Revit\Addins\2026\CQEING_CuadroCantidades.addin
   ```

   Este manifiesto contiene la ruta absoluta del ensamblado compilado, lo que le indica a Revit que debe cargar el Addin en el siguiente inicio del software.

---

## 📂 Estructura del Proyecto

* **`AddinManifest/`**: Plantillas `.addin` utilizadas para la generación del manifiesto de Revit.
* **`App/`**: Código de arranque del Addin (`RevitApplication.cs`), registro de servicios e inyección de dependencias (`AppServiceProvider.cs`).
* **`Commands/`**: Contiene la implementación de los comandos externos (`IExternalCommand`) de Revit que activan el cuadro de cantidades.
* **`Core/`**: Entidades del dominio, constantes globales e interfaces de abstracción del negocio.
* **`Infrastructure/`**: Implementación de repositorios, unidad de trabajo (Unit of Work) y contexto de base de datos para Entity Framework Core en memoria.
* **`Services/`**: Servicios del ciclo de vida del documento Revit y administración de la ventana de interfaz de usuario.
* **`UI/`**: Vistas en XAML y ViewModels (enlace de datos) para la interfaz de usuario.
* **`Resources/`**: Archivos de recursos visuales del Addin (como el icono `rubik.png`).
