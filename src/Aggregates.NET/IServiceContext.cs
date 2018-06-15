﻿using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public interface IServiceContext
    {
        Aggregates.UnitOfWork.IDomain UoW { get; }
        Aggregates.UnitOfWork.IApplication App { get; }

        IContainer Container { get; }
    }
}
