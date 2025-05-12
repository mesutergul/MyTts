using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using AutoMapper;

namespace MyTts.Data.Interfaces{
public interface IEntity
{
    int Id { get; set; }
}

// Base model interface
public interface IModel
{
    int Id { get; set; }
}

// Base mapping interface
public interface IMapFrom<T> where T : IEntity
{
    void Mapping(Profile profile);
}

    public interface INews : IModel
    {
    }

}