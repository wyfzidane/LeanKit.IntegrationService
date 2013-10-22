//------------------------------------------------------------------------------
// <copyright company="LeanKit Inc.">
//     Copyright (c) LeanKit Inc.  All rights reserved.
// </copyright> 
//------------------------------------------------------------------------------

namespace IntegrationService.Util
{
    public interface IConfigurationProvider<T>
    {
        T GetConfiguration();
    }
}