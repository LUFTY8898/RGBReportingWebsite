namespace RGBReportingWebsite
{
    public class spreadsheetFunctions
    {
        public static string ExcelColumnNumberToLetter(int intCol)
        {
            int intFirstLetter = ((intCol - 1) / 26) + 64;
            int intSecondLetter = ((intCol - 1) % 26) + 65;
            char letter1 = (intFirstLetter > 64) ? (char)intFirstLetter : ' ';
            return string.Concat(letter1, (char)intSecondLetter).Trim();
        }
        public static int ExcelColumnLetterToNumber(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) throw new ArgumentNullException("columnName");
            char[] characters = columnName.ToUpperInvariant().ToCharArray();

            int sum = 0;

            for (int i = 0; i < characters.Length; i++)
            {
                sum *= 26;
                sum += (characters[i] - 'A' + 1);
            }

            return sum;

        }

    }

}
namespace RGBReportingWebsite.Server.Services
{
    public class SpreadsheetFunctions
    {
    }
}
