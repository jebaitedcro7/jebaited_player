// WPF projekti ne uvoze System.IO automatski (sukob s System.Windows.Shapes.Path).
// Ovaj projekt ne koristi Shapes.Path, pa je System.IO siguran kao globalni using.
global using System.IO;
global using System.Net.Http;
