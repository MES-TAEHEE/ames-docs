using System.Text;
using AMES.Data.Services.Srm;

namespace AMES.Web.Services;

/// <summary>SRM 내보내기 파일(.xls = CP949 HTML 테이블, .xlsx = OOXML) → 문자열 격자. 파일 형식 감지만 한다(열 판정은 AMES.Data).</summary>
public static class SrmFileReader
{
    public static List<string[]> ReadGrid(Stream stream)
    {
        if (XlsxSheetReader.IsXlsx(stream)) return XlsxSheetReader.ReadRows(stream);
        using var rd = new StreamReader(stream, Encoding.GetEncoding(51949));
        return SrmHtmlGrid.Read(rd.ReadToEnd());
    }
}
