using AutoMapper;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.Mappings;

public sealed class OrcamentoProfile : Profile
{
    public OrcamentoProfile()
    {
        CreateMap<OrcamentoServico, OrcamentoServicoViewModel>();

        CreateMap<Orcamento, OrcamentoViewModel>()
            .ForMember(destino => destino.Status, configuracao => configuracao.MapFrom(origem => (int)origem.Status))
            .ForMember(destino => destino.StatusDescricao, configuracao => configuracao.MapFrom(origem => origem.Status.ObterDescricao()));
    }
}
