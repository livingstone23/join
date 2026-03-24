using Microsoft.VisualBasic;


namespace IANotes;


/// <summary>
/// A test class to use with github copilot. 
/// This class is not used in the application, it is only for testing purposes. 
/// </summary>
public class ClassTest
{

    string name = "John Doe";
    int age = 30;

    string testVar = "test";
    
    public ClassTest()
    {
        ConcatenateStrings(name, testVar);
    }


    ///Create a function that get two string and return a string with the two strings concatenated.
    public string ConcatenateStrings(string str1, string str2)
    {

        var concatenatedString = $"{str1} {str2}";

        var length = concatenatedString.Length;

        return concatenatedString;
    }
    

    def  defInstance = new def("John", "Doe");
    



}

public class NewBaseType
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
}

public class def : NewBaseType
{
    public def(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }
}