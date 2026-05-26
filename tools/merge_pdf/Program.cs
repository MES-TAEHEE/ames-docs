using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

if (args.Length < 2) {
    Console.Error.WriteLine("Usage: merge_pdf <output.pdf> <input1.pdf> [input2.pdf ...]");
    return 1;
}

string outputPath = args[0];
string[] inputs = args[1..];

using var output = new PdfDocument();
output.Info.Title = "A-MES L3 Spec";
output.Info.Author = "SEOYON E-HWA";
output.Info.Subject = "Manufacturing Execution System — Detailed Design";
output.Info.Creator = "A-MES PDF Build Tool";

int totalPages = 0;
foreach (var path in inputs) {
    if (!File.Exists(path)) {
        Console.Error.WriteLine($"  ! missing: {path}");
        continue;
    }
    try {
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Console.WriteLine($"  + {Path.GetFileName(path)} ({input.PageCount} pages)");
        for (int i = 0; i < input.PageCount; i++) {
            output.AddPage(input.Pages[i]);
        }
        totalPages += input.PageCount;
    } catch (Exception ex) {
        Console.Error.WriteLine($"  ! {Path.GetFileName(path)}: {ex.Message}");
    }
}

output.Save(outputPath);
Console.WriteLine($"= {totalPages} pages → {outputPath}");
return 0;
