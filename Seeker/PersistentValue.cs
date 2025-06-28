using System;
using System.Text.Json;
using Android.Content;
using AndroidX.Preference;
using Object = Java.Lang.Object;

namespace Seeker;

public class PersistentValue<T>
{
    public string Key { get; }
    public T DefaultValue  { get; }
    
    private PersistentValueType valueType;
    private ISharedPreferences preferences;

    public T Value
    {
        get
        {
            switch (valueType)
            {
                case PersistentValueType.Bool:
                    var booleanValue = preferences!.GetBoolean(Key, Convert.ToBoolean(DefaultValue));
                    return (T)Convert.ChangeType(booleanValue, typeof(T));
                case PersistentValueType.Int:
                    var integerValue = preferences!.GetInt(Key, Convert.ToInt32(DefaultValue));
                    return (T)Convert.ChangeType(integerValue, typeof(T));
                case PersistentValueType.Float:
                    var floatValue = preferences!.GetFloat(Key, Convert.ToSingle(DefaultValue));
                    return (T)Convert.ChangeType(floatValue, typeof(T));
                case PersistentValueType.String:
                    var stringValue = preferences!.GetString(Key, Convert.ToString(DefaultValue));
                    return (T)Convert.ChangeType(stringValue, typeof(T));
                case PersistentValueType.Object:
                    var rawValue = preferences!.GetString(Key, Convert.ToString(DefaultValue))!;
                    return JsonSerializer.Deserialize<T>(rawValue);
                default:
                    throw new PersistentValueTypeException();
            }
        }
        set
        {
            var editor = preferences.Edit()!;
            switch (valueType)
            {
                case PersistentValueType.Bool:
                    editor.PutBoolean(Key, Convert.ToBoolean(value));
                    break;
                case PersistentValueType.Int:
                    editor.PutInt(Key, Convert.ToInt32(value));
                    break;
                case PersistentValueType.Float:
                    editor.PutFloat(Key, Convert.ToSingle(value));
                    break;
                case PersistentValueType.String:
                    editor.PutString(Key, Convert.ToString(value));
                    break;
                case PersistentValueType.Object:
                    editor.PutString(Key, JsonSerializer.Serialize(value));
                    break;
                default:
                    throw new PersistentValueTypeException();
            }
            
            editor.Commit();
            ValueChanged?.Invoke(value);
        }
    }

    public event Action<T> ValueChanged;

    public PersistentValue(Context context, string key, T defaultValue, bool listen = true)
    {
        DefaultValue = defaultValue;
        Key = key;
        preferences = PreferenceManager.GetDefaultSharedPreferences(context)!;

        if (typeof(T).IsAssignableTo(typeof(bool)))
        {
            valueType = PersistentValueType.Bool;
        }
        else if (typeof(T).IsAssignableTo(typeof(int)))
        {
            valueType = PersistentValueType.Int;
        }
        else if (typeof(T).IsAssignableTo(typeof(float)))
        {
            valueType = PersistentValueType.Float;
        }
        else if (typeof(T).IsAssignableTo(typeof(string)))
        {
            valueType = PersistentValueType.String;
        }
        else
        {
            valueType = PersistentValueType.Object;
        }

        if (listen)
        {
            preferences.RegisterOnSharedPreferenceChangeListener(new PreferenceChangeListener(this));
        }
    }

    public PersistentValue(Context context, int key, T defaultValue, bool listen = true)
        : this(context, context.GetString(key), defaultValue, listen)
    {
        // Intentional no-op
    }

    public void Reset()
    {
        Value = DefaultValue;
    }

    private enum PersistentValueType
    {
        Bool,
        Int,
        Float,
        String,
        Object
    }

    private class PersistentValueTypeException : Exception;
    
    private class PreferenceChangeListener(PersistentValue<T> parent) 
        : Object, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        public void OnSharedPreferenceChanged(ISharedPreferences sharedPreferences, string key)
        {
            if (parent.Key.Equals(key))
            {
                parent.ValueChanged?.Invoke(parent.Value);
            }
        }
    }
}
