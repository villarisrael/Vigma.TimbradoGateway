# Análisis de Variables JSON - CajaActopan vs Vigma.TimbradoGateway

## ⚠️ PROBLEMA ENCONTRADO

El servidor **Vigma.TimbradoGateway** espera **todo en minúsculas y sin camelCase**, pero el cliente **CajaActopan** está generando **camelCase con mayúsculas iniciales**.

Línea crítica del servidor (TimbradoService.cs:604):
```csharp
var rfcEmisor = jobj["emisor"]?["rfc"]?.Value<string>()?.Trim();
```

---

## 📋 COMPARATIVA DE VARIABLES

### SECCIÓN: **factura**

| Campo | Cliente Genera | Servidor Espera | Estado |
|-------|----------------|-----------------|--------|
| `Version` | `"4.0"` | `version` | ❌ CAMBIAR |
| `TipoDeComprobante` | `"I"` | `tipocomprobante` | ❌ CAMBIAR |
| `Serie` | `serieFactura` | `serie` | ❌ CAMBIAR |
| `Folio` | `nuevoFolio` | `folio` | ❌ CAMBIAR |
| `Fecha` | `DateTime.Now.ToString()` | `fecha_expedicion` | ❌ CAMBIAR |
| `FormaPago` | `"01"` | `forma_pago` | ❌ CAMBIAR |
| `MetodoPago` | `"PUE"` | `metodo_pago` | ❌ CAMBIAR |
| `SubTotal` | `"560.00"` | `subtotal` | ❌ CAMBIAR |
| `Moneda` | `"MXN"` | `moneda` | ❌ CAMBIAR |
| `TipoCambio` | `"1.0000"` | `tipocambio` | ❌ CAMBIAR |
| `Total` | `"560.00"` | `total` | ❌ CAMBIAR |
| `LugarExpedicion` | `"42130"` | `LugarExpedicion` | ✅ OK (mayúsculas) |
| `Exportacion` | `"01"` | `Exportacion` | ✅ OK (mayúsculas) |

---

### SECCIÓN: **emisor**

| Campo | Cliente Genera | Servidor Espera | Estado |
|-------|----------------|-----------------|--------|
| `rfc` | `rfcEmisor` | `rfc` | ✅ OK (ya está minúsculas) |
| `Nombre` | `nombreEmisor` | `nombre` | ❌ CAMBIAR |
| `RegimenFiscal` | `regimenEmisor` | `RegimenFiscal` | ✅ OK |

---

### SECCIÓN: **receptor**

| Campo | Cliente Genera | Servidor Espera | Estado |
|-------|----------------|-----------------|--------|
| `Rfc` | `req.RfcReceptor` | `rfc` | ❌ CAMBIAR |
| `Nombre` | `req.NombreReceptor` | `nombre` | ❌ CAMBIAR |
| `DomicilioFiscalReceptor` | `req.CpReceptor` | `DomicilioFiscalReceptor` | ✅ OK |
| `RegimenFiscalReceptor` | `req.RegimenFiscalReceptor` | `RegimenFiscalReceptor` | ✅ OK |
| `UsoCFDI` | `req.UsoCFDI` | `UsoCFDI` | ✅ OK |

---

### SECCIÓN: **conceptos** (array)

| Campo | Cliente Genera | Servidor Espera | Estado |
|-------|----------------|-----------------|--------|
| `ClaveProdServ` | `"93151512"` | `ClaveProdServ` | ✅ OK |
| `ClaveUnidad` | `"E48"` | `ClaveUnidad` | ✅ OK |
| `Cantidad` | `"1.00"` | `cantidad` | ❌ CAMBIAR |
| `Descripcion` | `"AUDICIONES..."` | `descripcion` | ❌ CAMBIAR |
| `ValorUnitario` | `"560.00"` | `valorunitario` | ❌ CAMBIAR |
| `Importe` | `"560.00"` | `importe` | ❌ CAMBIAR |
| `ObjetoImp` | `"01"` | `ObjetoImp` | ✅ OK |

---

### SECCIÓN: **impuestos** (global, si aplica)

| Campo | Cliente Genera | Servidor Espera | Estado |
|-------|----------------|-----------------|--------|
| `TotalImpuestosTrasladados` | `totalIva.ToString()` | `TotalImpuestosTrasladados` | ✅ OK |
| `Traslados` | array | `translados` | ⚠️ ATENCIÓN: "translados" está mal escrito en el DTO |

---

## 🔧 CORRECCIONES NECESARIAS EN CajaActopan

En el método `TimbrarPorJsonAsync`, cambiar el objeto `cfdiJson`:

```csharp
var cfdiJson = new JsonObject
{
    ["factura"] = new JsonObject
    {
        ["version"]           = "4.0",              // ← CAMBIAR de "Version"
        ["tipocomprobante"]   = "I",                // ← CAMBIAR de "TipoDeComprobante"
        ["serie"]             = serieFactura,       // ← CAMBIAR de "Serie"
        ["folio"]             = nuevoFolio.ToString(), // ← CAMBIAR de "Folio"
        ["fecha_expedicion"]  = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), // ← CAMBIAR de "Fecha"
        ["forma_pago"]        = req.FormaPago,      // ← CAMBIAR de "FormaPago"
        ["metodo_pago"]       = req.MetodoPago,     // ← CAMBIAR de "MetodoPago"
        ["subtotal"]          = subtotal.ToString("F2"), // ← CAMBIAR de "SubTotal"
        ["moneda"]            = "MXN",              // ← CAMBIAR de "Moneda"
        ["tipocambio"]        = "1.0000",           // ← CAMBIAR de "TipoCambio"
        ["total"]             = total.ToString("F2"), // ← CAMBIAR de "Total"
        ["LugarExpedicion"]   = lugarExp,           // ✅ MANTENER
        ["Exportacion"]       = "01"                // ✅ MANTENER
    },
    ["emisor"] = new JsonObject
    {
        ["rfc"]           = rfcEmisor,              // ✅ OK
        ["nombre"]        = nombreEmisor,          // ← CAMBIAR de "Nombre"
        ["RegimenFiscal"] = regimenEmisor          // ✅ MANTENER
    },
    ["receptor"] = new JsonObject
    {
        ["rfc"]                     = req.RfcReceptor,        // ← CAMBIAR de "Rfc"
        ["nombre"]                  = req.NombreReceptor,      // ← CAMBIAR de "Nombre"
        ["DomicilioFiscalReceptor"] = req.CpReceptor,         // ✅ MANTENER
        ["RegimenFiscalReceptor"]   = req.RegimenFiscalReceptor, // ✅ MANTENER
        ["UsoCFDI"]                 = req.UsoCFDI              // ✅ MANTENER
    },
    ["conceptos"] = conceptosJson
};
```

### Para los **conceptos**:

```csharp
foreach (var c in req.Conceptos)
{
    var concepto = new JsonObject
    {
        ["ClaveProdServ"]  = c.ClaveProdServ,      // ✅ OK
        ["ClaveUnidad"]    = c.ClaveUnidad,        // ✅ OK
        ["cantidad"]       = c.Cantidad.ToString("F2"), // ← CAMBIAR de "Cantidad"
        ["descripcion"]    = c.Descripcion,        // ← CAMBIAR de "Descripcion"
        ["valorunitario"]  = c.ValorUnitario.ToString("F2"), // ← CAMBIAR de "ValorUnitario"
        ["importe"]        = c.Importe.ToString("F2"), // ← CAMBIAR de "Importe"
        ["ObjetoImp"]      = c.CobraIva ? "02" : "01"  // ✅ OK
    };
    // ... resto del concepto
}
```

---

## ⚠️ NOTA IMPORTANTE: DTO en Vigma (JsonTimbradoRequest.cs:92)

Hay un typo en el DTO del servidor:
```csharp
public List<TrasladoResumenDto>? translados { get; set; } = new();  // ❌ "translados" está mal escrito
```

Debería ser `traslados` (una sola 's'), pero eso es un error del servidor. Si lo necesitas, espera a que lo corrijan en Vigma.

---

## 📊 RESUMEN

- **17 campos que cambiar a minúsculas**
- **5 campos que dejan como están**
- **2 campos que eliminar o ajustar (LugarExpedicion y Exportacion)**
